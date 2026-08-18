using System;
#nullable enable

namespace Xaml.Behaviors.SourceGenerators
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Assembly, AllowMultiple = true)]
    internal sealed class GenerateObservableTriggerAttribute : Attribute
    {
        public string? Name { get; set; }
        public bool UseDispatcher { get; set; } = true;
        public bool FireOnAttach { get; set; } = true;

        public GenerateObservableTriggerAttribute()
        {
        }

        public GenerateObservableTriggerAttribute(Type targetType, string propertyName)
        {
        }
    }
}