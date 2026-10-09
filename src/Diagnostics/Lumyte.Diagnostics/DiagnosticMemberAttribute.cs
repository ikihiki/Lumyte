namespace Lumyte.Diagnostics;

/// <summary>Optionally overrides a scalar name or constraint.</summary>
/// <param name="id">The id argument.</param>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class DiagnosticMemberAttribute(string? id = null) : Attribute
{
    /// <summary>Gets the optional name.</summary>
    public string? Id { get; } = id;

    /// <summary>Gets or sets the maximum string length; zero uses the global limit.</summary>
    public int MaxLength { get; set; }
}
