using System;
using System.Reflection;

namespace Lib.CrossPatcher
{
    public static class ReflectionExtensions
    {
        public static MethodInfo GetPrivateMethod(this Type type, string methodName)
        {
            var methodInfo = type.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);

            if (methodInfo is null)
                return null;

            return methodInfo;
        }
        
        public static object GetPrivateField(this Type type,  object instance, string fieldName)
        {
            var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);

            if (field is null)
                return null;

            return field.GetValue(instance);
        }

        public static bool SetPrivateStaticField(this Type type, string fieldName, object value)
        {
            var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);

            if (field is null)
                return false;

            field.SetValue(null, value);
            return true;
        }
        
        public static bool SetPublicStaticField(this Type type, string fieldName, object value)
        {
            var field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Static);

            if (field is null)
                return false;

            field.SetValue(null, value);
            return true;
        }
        
        public static bool SetPrivateField(this Type type, object instance, string fieldName, object value)
        {
            var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);

            if (field is null)
                return false;

            field.SetValue(instance, value);
            return true;
        }
    }
}