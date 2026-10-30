namespace Lumyte.Composition;

/// <summary>Selects the host-owned base class used to wrap composable functions returning this type.</summary>
/// <param name="adapterType">A public abstract subclass with a protected parameterless constructor, a protected abstract Build method, and a public writable string Key.</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class ComposeFunctionAdapterAttribute(Type adapterType) : Attribute
{
    /// <summary>Gets the function wrapper base type.</summary>
    public Type AdapterType { get; } = adapterType;
}
