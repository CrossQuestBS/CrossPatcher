using System;

namespace Lib.CrossPatcher
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
    public class CrossPrefixAttribute : Attribute
    {
        public CrossPrefixAttribute()
        { }
    }
}