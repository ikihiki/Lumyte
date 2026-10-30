namespace Lumyte.Composition;

/// <summary>Selects a public one-argument generic value adapter for mutable value parameters.</summary>
/// <param name="adapterType">The unbound generic struct implementing IComposeArgument.</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class ComposeArgumentAdapterAttribute(Type adapterType) : Attribute
{
    /// <summary>Gets the unbound adapter type.</summary>
    public Type AdapterType { get; } = adapterType;
}
