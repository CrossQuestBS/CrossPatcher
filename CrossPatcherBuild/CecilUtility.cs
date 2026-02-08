using System.Collections.ObjectModel;
using System.Linq;
using Mono.Cecil;
using UnityEngine;

namespace CrossPatcherBuild
{
    public static class CecilUtility
    {

        public static string GetAttributeTypeName(this CustomAttribute attribute)
        {
            return attribute.AttributeType.Name;
        }
        
        public static CustomAttribute? GetCustomAttribute(this MethodDefinition method, string name)
        {
            return method.CustomAttributes.FirstOrDefault(it => it.AttributeType.Name == name);
        }
        
        public static CustomAttribute[] GetCustomAttributes(this MethodDefinition method, string[] names)
        {
            return method.CustomAttributes.Where(it => names.Contains(it.AttributeType.Name)).ToArray();
        }
        
        public static TypeDefinition GetType(this AssemblyDefinition asm, string name)
        {
            var type = asm.MainModule.Types.FirstOrDefault(t => t.Name == name);

            if (type is  null)
                Debug.LogError("Failed to get type: " + name + " in assembly: " + asm.Name);
        
            return type;
        }

        public static MethodDefinition GetMethod(this TypeDefinition typeDef, string methodName)
        {
            var method = typeDef.Methods.FirstOrDefault(t => t.Name == methodName);
            if (method is  null)
                Debug.LogError("Failed to get method: " + methodName + " in type: " + typeDef.Name);

            return method;
        }
        public static MethodDefinition GetMethod(AssemblyDefinition asm, string typeName, string methodName)
        {
            var type = GetType(asm, typeName);

            if (type is null)
                return null;

            var method = GetMethod(type, methodName);

            return method;
        }

        public static FieldDefinition GetField(TypeDefinition typeDef, string fieldName)
        {
            var method = typeDef.Fields.FirstOrDefault(t => t.Name == fieldName);
            if (method is  null)
                Debug.LogError("Failed to get field: " + fieldName + " in type: " + typeDef.Name);

            return method;
        }
    }
}