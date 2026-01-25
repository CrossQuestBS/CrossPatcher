using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Lib.CrossPatcher;
using Mono.Cecil;
using Mono.Cecil.Cil;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using FieldAttributes = Mono.Cecil.FieldAttributes;
using OpCodes = Mono.Cecil.Cil.OpCodes;

namespace Patch.CrossPatcher
{
    public class Patcher : IPostBuildPlayerScriptDLLs
    {
        public int callbackOrder => 0;

        public DefaultAssemblyResolver Resolver = new DefaultAssemblyResolver();
        
    
        public static string FirstCharToLowerCase(string str)
        {
            if ( !string.IsNullOrEmpty(str) && char.IsUpper(str[0]))
                return str.Length == 1 ? char.ToLower(str[0]).ToString() : char.ToLower(str[0]) + str[1..];

            return str;
        }
        
        public void OnPostBuildPlayerScriptDLLs(BuildReport report)
        {
            var assemblyPath = report.GetFiles();
            var modAssemblies = assemblyPath.Where(it => Path.GetFileName(it.path).StartsWith("Mod.") && it.path.EndsWith(".dll"));

            var zenject = assemblyPath.FirstOrDefault(t => t.path.Contains("Zenject-usage.dll"));
            
           
            
            foreach (var modAssembly in modAssemblies)
            {
                Resolver = new DefaultAssemblyResolver();
                var assemblyParentPath = Directory.GetParent(zenject.path);

                if (assemblyParentPath is null)
                {
                    Debug.LogError("Failed to get parent path for: " + zenject.path);
                    return;
                }

                Resolver.AddSearchDirectory(assemblyParentPath.FullName);
                
                using var zenjectAssembly = AssemblyDefinition.ReadAssembly(zenject.path,
                    new ReaderParameters { ReadWrite = true, InMemory = true, AssemblyResolver = Resolver });
                
                Debug.Log("Trying on assembly: " + Path.GetFileName(modAssembly.path));
                var mainAssemblyPath = modAssembly.path;
                
                using var assembly = AssemblyDefinition.ReadAssembly(mainAssemblyPath,
                    new ReaderParameters { ReadWrite = true, InMemory = true, AssemblyResolver = Resolver });
                ApplyCrossPatch(assembly, zenjectAssembly, assemblyParentPath.FullName);
                Resolver.Dispose();
            }
        }

        private string GetResolvedTypeAssemblyName(TypeReference typeReference)
        {
            var elementType = typeReference.GetElementType();
            var scope = elementType.Scope;
            var name = (AssemblyNameReference)scope;
            return name.Name;
        }

        private void InjectField(TypeDefinition injectField, MethodDefinition zenjectAttributeCtor, TypeDefinition instance)
        {

            var newFieldName = $"_{FirstCharToLowerCase(injectField.Name)}";
            if (instance.Fields.Any(it => it.Name == newFieldName))
                return;
            
            var newField = new FieldDefinition(newFieldName, FieldAttributes.Private, injectField);
            instance.Fields.Add(newField);
            
            var attributeCtorRef = instance.Module.ImportReference(zenjectAttributeCtor);
            var injectAttribute = new CustomAttribute(attributeCtorRef);
            newField.CustomAttributes.Add(injectAttribute);
        }

        private AssemblyDefinition GetAssemblyDefinition(string path, Dictionary<string, AssemblyDefinition> assembliesOpen)
        {
            if (assembliesOpen.TryGetValue(path, out var definition))
            {
                return definition;
            }
            
            var assembly = AssemblyDefinition.ReadAssembly(path,
                new ReaderParameters { ReadWrite = true, InMemory = true, AssemblyResolver = Resolver });
            
            assembliesOpen.Add(path, assembly);

            return assembly;
        }
        
        private void ApplyPostFix(MethodDefinition method, MethodDefinition patchMethod)
        {
            Debug.Log("Running Postfix on: " + method.FullName);

            var ilProcessor = method.Body.GetILProcessor();
            var reference = method.Module.ImportReference(patchMethod);
                    
            // Removes return
            ilProcessor.Remove(method.Body.Instructions.Last());
            
            for (int i = 0; i < patchMethod.Parameters.Count; i++)
            {
                ilProcessor.Emit(OpCodes.Ldarg, i);
            }
            
            ilProcessor.Emit(OpCodes.Call, reference);
            ilProcessor.Emit(OpCodes.Ret);
        }

        private void ApplyPrefix(MethodDefinition method, MethodDefinition patchMethod)
        {
            Debug.Log("Running Prefix on: " + method.FullName);

            var ilProcessor = method.Body.GetILProcessor();
            var reference = method.Module.ImportReference(patchMethod);

            var instruction = method.Body.Instructions[0];
            

            var hasResultByRef =
                patchMethod.Parameters.Any(it => it.Name == "__result" && it.ParameterType.IsByReference);


            VariableDefinition localResultVariable = null;

            if (hasResultByRef)
            {
                localResultVariable = new VariableDefinition(method.ReturnType);
                method.Body.Variables.Add(localResultVariable);
            }
            
            if (method.ReturnType.IsByReference)
            {
                
                ilProcessor.InsertBefore(instruction, ilProcessor.Create(OpCodes.Ldc_I4_1));
                ilProcessor.InsertBefore(instruction, ilProcessor.Create(OpCodes.Newarr, method.ReturnType.GetElementType()));
                ilProcessor.InsertBefore(instruction, ilProcessor.Create(OpCodes.Ldc_I4_0));
                ilProcessor.InsertBefore(instruction, ilProcessor.Create(OpCodes.Ldelem_Ref, method.ReturnType.GetElementType()));
                ilProcessor.InsertBefore(instruction, ilProcessor.Create(OpCodes.Stloc, localResultVariable));
            }
            
            var hasBoolReturn = patchMethod.ReturnType.Name.ToLower().StartsWith("bool");
            
            
            if (hasResultByRef)
                ilProcessor.InsertBefore(instruction, ilProcessor.Create(OpCodes.Ldloca, localResultVariable));
            
            if (patchMethod.Parameters.Count > 0 && patchMethod.Parameters[0].Name.Contains("_instance"))
                ilProcessor.InsertBefore(instruction, ilProcessor.Create(OpCodes.Ldarg_0));
            
            ilProcessor.InsertBefore(instruction, ilProcessor.Create(OpCodes.Call, reference));

            if (hasBoolReturn)
            {
                ilProcessor.InsertBefore(instruction, ilProcessor.Create(OpCodes.Brtrue, instruction));
                if (hasResultByRef)
                {
                    ilProcessor.InsertBefore(instruction, ilProcessor.Create(OpCodes.Ldloc, localResultVariable));
                }

              
                ilProcessor.InsertBefore(instruction, ilProcessor.Create(OpCodes.Ret));
            }
        }
        
        
        private void ApplyCrossPatch(AssemblyDefinition modAssembly, AssemblyDefinition zenject, string rootPath)
        {
            var types = modAssembly.MainModule.Types.Where(it => it.HasInterfaces && it.Interfaces.Any(it => it.InterfaceType.Name.Contains("ICrossPatch")));
            var zenjectAttribute = PatchUtils.GetType(zenject, "InjectAttribute");
            var zenjectAttributeCtor = zenjectAttribute.Methods.FirstOrDefault(t => t.IsConstructor);
            
            foreach (var type in types)
            {
                Debug.Log("Trying on type: " + type.FullName);

                var patchedMethods = type.Methods.Where(m => m.CustomAttributes.Any(ca => ca.AttributeType.Name == "CrossPatchAttribute")).ToArray();

                foreach (var hookMethod in patchedMethods)
                {
                    Dictionary<string, AssemblyDefinition> assembliesOpen = new Dictionary<string, AssemblyDefinition>();

                    Debug.Log("Trying on patched Method: " + hookMethod.FullName);

                    var attribute = hookMethod.CustomAttributes.FirstOrDefault(it => it.AttributeType.Name == "CrossPatchAttribute");

                    if (attribute is null)
                        continue;
                    
                    var instanceTypeReference = (TypeReference)attribute.ConstructorArguments[0].Value;
                    
                    Debug.Log("Type: " + instanceTypeReference.FullName);
                    
                    var instanceAssemblyPath =
                        Path.Join(rootPath, GetResolvedTypeAssemblyName(instanceTypeReference) + ".dll");
                    var instanceAssembly = GetAssemblyDefinition(instanceAssemblyPath, assembliesOpen);
                    var instanceType = PatchUtils.GetType(instanceAssembly, instanceTypeReference.Name); 
                    
                    
                    // Injecting!
                    
                    if (attribute.ConstructorArguments[2].Value is CustomAttributeArgument[] injectTypes)
                    {
                        foreach (var argumentType in injectTypes)
                        {
                            if (argumentType.Value is not TypeReference injectedTypeReference)
                                continue;

                            var assemblyName = GetResolvedTypeAssemblyName(injectedTypeReference);
                            var assemblyPath = Path.Join(rootPath, assemblyName + ".dll");
                        
                            var assembly = GetAssemblyDefinition(assemblyPath, assembliesOpen);
                            var injectedType = PatchUtils.GetType(assembly, injectedTypeReference.Name);
                        
                            InjectField(injectedType, zenjectAttributeCtor, instanceType);
                        }
                    }

                    var methodName = (string)attribute.ConstructorArguments[1].Value;
                    var methodToPatch = PatchUtils.GetMethod(instanceType, methodName);

                    // If is POSTFIX
                    if (hookMethod.CustomAttributes.Any(it => it.AttributeType.Name == "CrossPostfixAttribute"))
                    {

                        ApplyPostFix(methodToPatch, hookMethod);
                    }

                    if (hookMethod.CustomAttributes.Any(it => it.AttributeType.Name == "CrossPrefixAttribute"))
                    {
                        ApplyPrefix(methodToPatch, hookMethod);
                    }
                    
                    instanceAssembly.Write(instanceAssemblyPath);
                    foreach (var assembly in assembliesOpen.Values)
                    {
                        assembly.Dispose();
                    }
                    assembliesOpen.Clear();
                }
                
            }
            
        }
    }
}
