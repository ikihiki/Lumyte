namespace Lumyte.Composition;

/// <summary>Defines the default generated factory class for an assembly.</summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class CompositionDefaultsAttribute(string factoryClass) : Attribute
{
    public string FactoryClass { get; } = string.IsNullOrWhiteSpace(factoryClass)
        ? throw new ArgumentException("A factory class name is required.", nameof(factoryClass))
        : factoryClass;
}
