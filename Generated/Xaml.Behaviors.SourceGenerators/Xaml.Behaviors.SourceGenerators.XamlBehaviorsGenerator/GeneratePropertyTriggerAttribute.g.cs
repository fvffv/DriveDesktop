using System;
#nullable enable

namespace Xaml.Behaviors.SourceGenerators
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Assembly, AllowMultiple = true)]
    internal sealed class GeneratePropertyTriggerAttribute : Attribute
    {
        public string? Name { get; set; }
        public string? SourceName { get; set; }
        public bool UseDispatcher { get; set; }

        public GeneratePropertyTriggerAttribute()
        {
        }

        public GeneratePropertyTriggerAttribute(Type targetType, string propertyName)
        {
        }
    }
}