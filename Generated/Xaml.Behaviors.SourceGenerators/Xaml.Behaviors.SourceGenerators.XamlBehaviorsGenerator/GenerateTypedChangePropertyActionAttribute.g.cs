using System;
#nullable enable

namespace Xaml.Behaviors.SourceGenerators
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Assembly, AllowMultiple = true)]
    internal class GenerateTypedChangePropertyActionAttribute : Attribute
    {
        public bool UseDispatcher { get; set; }

        public GenerateTypedChangePropertyActionAttribute()
        {
        }

        public GenerateTypedChangePropertyActionAttribute(Type targetType, string propertyName)
        {
        }
    }
}