namespace Lumyte.Composition;

/// <summary>Defines the default outer factory class name for an assembly.</summary>
/// <param name="factoryClass">The nonempty outer factory class name.</param>
/// <exception cref="ArgumentException">The name is null, empty, or whitespace.</exception>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class CompositionDefaultsAttribute(string factoryClass) : Attribute
{
    /// <summary>Gets the default outer factory class name.</summary>
    public string FactoryClass { get; } = string.IsNullOrWhiteSpace(factoryClass)
        ? throw new ArgumentException("A factory class name is required.", nameof(factoryClass))
        : factoryClass;
}
