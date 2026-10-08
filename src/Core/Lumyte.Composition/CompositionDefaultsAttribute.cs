namespace Lumyte.Composition;

/// <summary>Defines the default outer factory class name for an assembly.</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class CompositionDefaultsAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="CompositionDefaultsAttribute"/> class.</summary>
    /// <param name="factoryClass">The nonempty outer factory class name.</param>
    /// <exception cref="ArgumentException">The name is null, empty, or whitespace.</exception>
    public CompositionDefaultsAttribute(string factoryClass)
    {
        FactoryClass = string.IsNullOrWhiteSpace(factoryClass)
            ? throw new ArgumentException("A factory class name is required.", nameof(factoryClass))
            : factoryClass;
    }

    /// <summary>Gets the default outer factory class name.</summary>
    public string FactoryClass { get; }
}
