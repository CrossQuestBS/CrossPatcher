using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using FieldAttributes = Mono.Cecil.FieldAttributes;
using OpCodes = Mono.Cecil.Cil.OpCodes;

namespace CrossPatcherBuild
{
    public class Patcher : IPostBuildPlayerScriptDLLs
    {
        public int callbackOrder => 0;
        private DefaultAssemblyResolver _resolver;

        private static string ToCamelCase(string str)
        {
            if (!string.IsNullOrEmpty(str) && char.IsUpper(str[0]))
                return str.Length == 1 ? char.ToLower(str[0]).ToString() : char.ToLower(str[0]) + str[1..];

            return str;
        }

        public void InitializeResolver(string assemblyParentPath)
        {
            _resolver = new DefaultAssemblyResolver();

            _resolver.AddSearchDirectory(assemblyParentPath);
        }

        private AssemblyDefinition ReadAssembly(string path)
        {
            return AssemblyDefinition.ReadAssembly(path,
                new ReaderParameters { ReadWrite = true, InMemory = true, AssemblyResolver = _resolver });
        }

        public void OnPostBuildPlayerScriptDLLs(BuildReport report)
        {
            var assemblyPath = report.GetFiles();

            var modfolder = Path.Combine(Application.dataPath, "Plugins", "Mods");

            var modFolderFiles = Directory.GetFiles(modfolder).Select(modFilePath => Path.GetFileName(modFilePath)).Where(it => it.EndsWith(".dll")).ToList();
            
            Debug.LogWarning("Found files: " + string.Join(",", modFolderFiles));
            var modAssemblies = assemblyPath.Where(it =>
                it.path.EndsWith(".dll") && modFolderFiles.Any(modFile => modFile == Path.GetFileName(it.path)));
            
            var zenject = assemblyPath.FirstOrDefault(t => t.path.Contains("Zenject-usage.dll"));

            var assemblyParentPath = Directory.GetParent(zenject.path);

            if (assemblyParentPath is null)
            {
                Debug.LogError("Failed to get parent path for: " + zenject);
                return;
            }

            foreach (var modAssembly in modAssemblies)
            {
                Debug.Log("Trying out assembly: " + Path.GetFileName(modAssembly.path));
                InitializeResolver(assemblyParentPath.FullName);
                using var zenjectAssembly = ReadAssembly(zenject.path);

                var mainAssemblyPath = modAssembly.path;
                using var assembly = ReadAssembly(mainAssemblyPath);

                ApplyCrossPatch(assembly, zenjectAssembly, assemblyParentPath.FullName);
                _resolver.Dispose();
            }
        }

        private void ApplyCrossPatch(AssemblyDefinition modAssembly, AssemblyDefinition zenject, string rootPath)
        {
            var crossPatchTypes = modAssembly.MainModule.Types.Where(it =>
                it.HasInterfaces &&
                it.Interfaces.Any(interfaceImpl => interfaceImpl.InterfaceType.Name.Contains("ICrossPatch"))).ToArray();

            if (crossPatchTypes.Length == 0)
                return;

            var zenjectAttribute = zenject.GetType("InjectAttribute");
            var zenjectAttributeCtor = zenjectAttribute.Methods.First(t => t.IsConstructor);

            foreach (var crossPatchType in crossPatchTypes)
            {
                var patchedMethods = crossPatchType.Methods
                    .Where(m => m.GetCustomAttribute("CrossPatchAttribute") is not null).ToArray();

                foreach (var hookMethod in patchedMethods)
                {
                    // Check if attribute is on the object
                    var patchAttribute = hookMethod.GetCustomAttribute("CrossPatchAttribute");

                    if (patchAttribute is null)
                        continue;

                    var assembliesOpen = new Dictionary<string, AssemblyDefinition>();

                    // Parse patch attribute
                    var patchTypeReference = (TypeReference)patchAttribute.ConstructorArguments[0].Value;
                    var methodName = (string)patchAttribute.ConstructorArguments[1].Value;


                    // Get assembly to patch
                    var patchAssemblyPath =
                        Path.Join(rootPath, GetResolvedTypeAssemblyName(patchTypeReference) + ".dll");
                    var patchAssembly = GetAssemblyDefinition(patchAssemblyPath, assembliesOpen);

                    var patchType = patchAssembly.GetType(patchTypeReference.Name);
                    var patchMethod = patchType.GetMethod(methodName);

                    if (patchAttribute.ConstructorArguments.Count == 3)
                    {
                        var customAttributeArguments = patchAttribute.ConstructorArguments[2].Value;
                        if (customAttributeArguments is CustomAttributeArgument[] injectTypes)
                        {
                            InjectFields(rootPath, injectTypes, assembliesOpen, zenjectAttributeCtor, patchType);
                        }
                    }

                    HandlePatchAttribute(patchMethod, hookMethod);

                    patchAssembly.Write(patchAssemblyPath);

                    foreach (var assembly in assembliesOpen.Values)
                    {
                        assembly.Dispose();
                    }

                    assembliesOpen.Clear();
                }
            }
        }

        private string GetResolvedTypeAssemblyName(TypeReference typeReference)
        {
            var elementType = typeReference.GetElementType();
            var scope = elementType.Scope;
            var name = (AssemblyNameReference)scope;
            return name.Name;
        }

        private void InjectField(TypeDefinition field, MethodDefinition zenjectAttributeMethod, TypeDefinition type)
        {
            var injectFieldName = $"_{ToCamelCase(field.Name)}";

            if (type.Fields.Any(it => it.Name == injectFieldName))
                return;

            var newField = new FieldDefinition(injectFieldName, FieldAttributes.Private, field);
            type.Fields.Add(newField);

            newField.CustomAttributes.Add(new CustomAttribute(type.Module.ImportReference(zenjectAttributeMethod)));
        }

        private AssemblyDefinition GetAssemblyDefinition(string path,
            Dictionary<string, AssemblyDefinition> assembliesOpen)
        {
            if (assembliesOpen.TryGetValue(path, out var definition))
            {
                return definition;
            }

            var assembly = ReadAssembly(path);

            assembliesOpen.Add(path, assembly);

            return assembly;
        }

        private void ApplyPostFix(MethodDefinition method, MethodDefinition patchMethod)
        {
            Debug.Log("Running Postfix on: " + method.FullName);

            var ilProcessor = method.Body.GetILProcessor();
            var reference = method.Module.ImportReference(patchMethod);

            ilProcessor.Remove(method.Body.Instructions.Last());

            for (var i = 0; i < patchMethod.Parameters.Count; i++)
            {
                ilProcessor.Emit(OpCodes.Ldarg, i);
            }

            ilProcessor.Emit(OpCodes.Call, reference);
            ilProcessor.Emit(OpCodes.Ret);
        }

        private VariableDefinition CreateLocalResultVariable(MethodDefinition method, ILProcessor ilProcessor,
            Instruction instruction)
        {
            VariableDefinition localResultVariable = new VariableDefinition(method.ReturnType);
            method.Body.Variables.Add(localResultVariable);
            if (!method.ReturnType.IsByReference) return localResultVariable;

            Instruction[] instructions =
            {
                ilProcessor.Create(OpCodes.Ldc_I4_1),
                ilProcessor.Create(OpCodes.Newarr, method.ReturnType.GetElementType()),
                ilProcessor.Create(OpCodes.Ldc_I4_0),
                ilProcessor.Create(OpCodes.Ldelem_Ref, method.ReturnType.GetElementType()),
                ilProcessor.Create(OpCodes.Stloc, localResultVariable),
            };

            foreach (var newInstruction in instructions)
            {
                ilProcessor.InsertBefore(instruction, newInstruction);
            }

            return localResultVariable;
        }

        private void InsertPrefixPatchStack(MethodDefinition patchMethod, ILProcessor ilProcessor,
            Instruction instruction, bool hasReturnParameter, VariableDefinition? localResultVariable)
        {
            var parameters = patchMethod.Parameters.ToArray().Where(it => !it.Name.Contains("__result")).ToArray();

            for (var i = 0; i < parameters.Length; i++)
            {
                ilProcessor.InsertBefore(instruction,
                    ilProcessor.Create(OpCodes.Ldarg, i));
            }

            if (hasReturnParameter)
                ilProcessor.InsertBefore(instruction,
                    ilProcessor.Create(OpCodes.Ldloca, localResultVariable));
        }


        private static void HandlePrefixReturn(ILProcessor ilProcessor, Instruction instruction,
            bool hasReturnParameter,
            VariableDefinition? localResultVariable)
        {
            ilProcessor.InsertBefore(instruction, ilProcessor.Create(OpCodes.Brtrue, instruction));

            if (hasReturnParameter)
            {
                ilProcessor.InsertBefore(instruction, ilProcessor.Create(OpCodes.Ldloc, localResultVariable));
            }

            ilProcessor.InsertBefore(instruction, ilProcessor.Create(OpCodes.Ret));
        }

        private void ApplyPrefix(MethodDefinition method, MethodDefinition patchMethod)
        {
            var ilProcessor = method.Body.GetILProcessor();
            var reference = method.Module.ImportReference(patchMethod);

            var instruction = method.Body.Instructions[0];

            var hasReturnParameter =
                patchMethod.Parameters.Any(it => it.Name == "__result" && it.ParameterType.IsByReference);

            var localResultVariable =
                hasReturnParameter ? CreateLocalResultVariable(method, ilProcessor, instruction) : null;

            var hasBoolReturn = patchMethod.ReturnType.Name.ToLower().StartsWith("bool");

            InsertPrefixPatchStack(patchMethod, ilProcessor, instruction, hasReturnParameter, localResultVariable);

            ilProcessor.InsertBefore(instruction, ilProcessor.Create(OpCodes.Call, reference));

            if (hasBoolReturn)
                HandlePrefixReturn(ilProcessor, instruction, hasReturnParameter, localResultVariable);
        }

        private void HandlePatchAttribute(MethodDefinition methodToPatch, MethodDefinition hookMethod)
        {
            string[] patchAttributes = { "CrossPostfixAttribute", "CrossPrefixAttribute" };
            var attributes = hookMethod.GetCustomAttributes(patchAttributes);

            if (attributes.Length == 0)
                return;

            if (attributes.Length > 1)
                throw new Exception("Expected either Postfix or Prefix attribute, got both");

            var attributeName = attributes[0].GetAttributeTypeName();

            switch (attributeName)
            {
                case "CrossPostfixAttribute":
                    ApplyPostFix(methodToPatch, hookMethod);
                    break;
                case "CrossPrefixAttribute":
                    ApplyPrefix(methodToPatch, hookMethod);
                    break;
            }
        }


        private void InjectFields(string rootPath, CustomAttributeArgument[] injectTypes,
            Dictionary<string, AssemblyDefinition> assembliesOpen,
            MethodDefinition zenjectAttributeCtor, TypeDefinition instanceType)
        {
            foreach (var argumentType in injectTypes)
            {
                if (argumentType.Value is not TypeReference injectedTypeReference)
                    continue;

                var assemblyName = GetResolvedTypeAssemblyName(injectedTypeReference);
                var assemblyPath = Path.Join(rootPath, assemblyName + ".dll");

                var assembly = GetAssemblyDefinition(assemblyPath, assembliesOpen);
                var injectedType = assembly.GetType(injectedTypeReference.Name);

                InjectField(injectedType, zenjectAttributeCtor, instanceType);
            }
        }
    }
}