namespace Lumyte.Diagnostics;

/// <summary>Optionally overrides a scalar name or constraint.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class DiagnosticMemberAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="DiagnosticMemberAttribute"/> class.</summary>
    /// <param name="id">The id argument.</param>
    public DiagnosticMemberAttribute(string? id = null)
    {
        Id = id;
    }

    /// <summary>Gets the optional name.</summary>
    public string? Id { get; }

    /// <summary>Gets or sets the maximum string length; zero uses the global limit.</summary>
    public int MaxLength { get; set; }
}
