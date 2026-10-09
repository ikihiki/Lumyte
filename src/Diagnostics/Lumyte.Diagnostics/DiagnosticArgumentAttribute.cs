namespace Lumyte.Diagnostics;

/// <summary>Optionally overrides a scalar name or constraint.</summary>
[AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
public sealed class DiagnosticArgumentAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="DiagnosticArgumentAttribute"/> class.</summary>
    /// <param name="id">The id argument.</param>
    public DiagnosticArgumentAttribute(string? id = null)
    {
        Id = id;
    }

    /// <summary>Gets the optional name.</summary>
    public string? Id { get; }

    /// <summary>Gets or sets the minimum numeric value.</summary>
    public double Minimum { get; set; } = double.NegativeInfinity;

    /// <summary>Gets or sets the maximum numeric value.</summary>
    public double Maximum { get; set; } = double.PositiveInfinity;

    /// <summary>Gets or sets the maximum string length; zero uses the global limit.</summary>
    public int MaxLength { get; set; }
}
