using System;

namespace CrossPatcher.Attributes;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public class CrossPatchAttribute : Attribute
{
    public Type DeclaringType { get; }
    public string MethodName { get; }
        
    public Type[] InjectedTypes { get; }
        
    internal bool Complete => DeclaringType != null && MethodName != null;


    public CrossPatchAttribute(Type declaringType, string methodName, Type[] injectedTypes)
    {
        DeclaringType = declaringType;
        MethodName = methodName;
        InjectedTypes = injectedTypes;
    }
        
    public CrossPatchAttribute(Type declaringType, string methodName)
    {
        DeclaringType = declaringType;
        MethodName = methodName;
        InjectedTypes = Type.EmptyTypes;
    }
}