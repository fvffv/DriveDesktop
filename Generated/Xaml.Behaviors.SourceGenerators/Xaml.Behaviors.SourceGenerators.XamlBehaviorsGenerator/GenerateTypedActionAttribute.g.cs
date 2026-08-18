using System;
#nullable enable

namespace Xaml.Behaviors.SourceGenerators
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Assembly, AllowMultiple = true)]
    internal class GenerateTypedActionAttribute : Attribute
    {
        public bool UseDispatcher { get; set; }

        public GenerateTypedActionAttribute()
        {
        }

        public GenerateTypedActionAttribute(Type targetType, string methodName)
        {
        }
    }
}