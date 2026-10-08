namespace Lumyte.Composition;

/// <summary>Generates a factory delegate extension that applies a static method to a constructed target.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ComposeActionAttribute : Attribute
{
}
