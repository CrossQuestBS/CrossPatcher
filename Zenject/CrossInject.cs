using System;

namespace Lib.CrossPatcher
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]

    public class CrossInject : Attribute
    {
        internal Type? InjectType { get; }

        internal bool Complete =>  InjectType != null;

        
        public CrossInject(Type injectType)
        {
            InjectType = injectType;
        }
    }
}