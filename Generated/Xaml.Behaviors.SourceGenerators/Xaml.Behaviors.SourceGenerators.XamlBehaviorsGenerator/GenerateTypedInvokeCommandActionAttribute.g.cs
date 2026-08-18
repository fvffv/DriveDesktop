using System;
#nullable enable

namespace Xaml.Behaviors.SourceGenerators
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    internal sealed class GenerateTypedInvokeCommandActionAttribute : Attribute
    {
        public bool UseDispatcher { get; set; }
    }

    [AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
    internal sealed class ActionCommandAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
    internal sealed class ActionParameterAttribute : Attribute
    {
    }
}