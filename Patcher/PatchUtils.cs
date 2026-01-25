using System.Linq;
using Mono.Cecil;
using UnityEngine;

namespace  Patch.CrossPatcher
{
    public class PatchUtils
    {
        public static TypeDefinition GetType(AssemblyDefinition asm, string name)
        {
            var type = asm.MainModule.Types.FirstOrDefault(t => t.Name == name);

            if (type is  null)
                Debug.LogError("Failed to get type: " + name + " in assembly: " + asm.Name);
        
            return type;
        }

        public static MethodDefinition GetMethod(TypeDefinition typeDef, string methodName)
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