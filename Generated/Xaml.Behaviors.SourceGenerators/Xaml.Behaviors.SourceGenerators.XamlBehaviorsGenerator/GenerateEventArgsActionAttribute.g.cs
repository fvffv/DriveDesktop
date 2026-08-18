using System;
#nullable enable

namespace Xaml.Behaviors.SourceGenerators
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Assembly, AllowMultiple = true)]
    internal sealed class GenerateEventArgsActionAttribute : Attribute
    {
        public string? Name { get; set; }
        public string? Project { get; set; }
        public bool UseDispatcher { get; set; }

        public GenerateEventArgsActionAttribute()
        {
        }

        public GenerateEventArgsActionAttribute(Type targetType, string methodName)
        {
        }
    }
}