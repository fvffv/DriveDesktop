using System;
#nullable enable

namespace Xaml.Behaviors.SourceGenerators
{
    [AttributeUsage(AttributeTargets.Event | AttributeTargets.Assembly, AllowMultiple = true)]
    internal sealed class GenerateEventCommandAttribute : Attribute
    {
        public string? Name { get; set; }
        public string? ParameterPath { get; set; }
        public bool UseDispatcher { get; set; }

        public GenerateEventCommandAttribute()
        {
        }

        public GenerateEventCommandAttribute(Type targetType, string eventName)
        {
        }
    }
}