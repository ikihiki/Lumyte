namespace Lumyte.Composition;

/// <summary>Selects construction argument adapters independently of declared parameter types.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class ComposeArgumentAdapterAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="ComposeArgumentAdapterAttribute"/> class for all parameter types.</summary>
    /// <param name="adapterType">A public unbound one-argument generic struct implementing IComposeArgument.</param>
    public ComposeArgumentAdapterAttribute(Type adapterType)
    {
        AdapterType = adapterType;
    }

    /// <summary>Initializes a new instance of the <see cref="ComposeArgumentAdapterAttribute"/> class for a matched type.</summary>
    /// <param name="parameterType">The declared type to match.</param>
    /// <param name="adapterType">A closed struct for an exact match, or an unbound generic struct for a generic match.</param>
    public ComposeArgumentAdapterAttribute(Type parameterType, Type adapterType)
    {
        ParameterType = parameterType;
        AdapterType = adapterType;
    }

    /// <summary>Gets the matched declared type, or null for the fallback adapter.</summary>
    public Type? ParameterType { get; }

    /// <summary>Gets the adapter type.</summary>
    public Type AdapterType { get; }
}
