using System;

namespace CrossPatcher.Attributes;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public class CrossPostfixAttribute : Attribute
{
    public CrossPostfixAttribute()
    { }
}