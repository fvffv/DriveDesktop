using System;
#nullable enable

namespace Xaml.Behaviors.SourceGenerators
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Assembly, AllowMultiple = true)]
    internal sealed class GenerateAsyncTriggerAttribute : Attribute
    {
        public string? Name { get; set; }
        public bool UseDispatcher { get; set; } = true;
        public bool FireOnAttach { get; set; } = true;

        public GenerateAsyncTriggerAttribute()
        {
        }

        public GenerateAsyncTriggerAttribute(Type targetType, string propertyName)
        {
        }
    }
}