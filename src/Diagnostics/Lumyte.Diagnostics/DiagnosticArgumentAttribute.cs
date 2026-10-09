namespace Lumyte.Diagnostics;

/// <summary>Optionally overrides a scalar name or constraint.</summary>
/// <param name="id">The id argument.</param>
[AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
public sealed class DiagnosticArgumentAttribute(string? id = null) : Attribute
{
    /// <summary>Gets the optional name.</summary>
    public string? Id { get; } = id;

    /// <summary>Gets or sets the minimum numeric value.</summary>
    public double Minimum { get; set; } = double.NegativeInfinity;

    /// <summary>Gets or sets the maximum numeric value.</summary>
    public double Maximum { get; set; } = double.PositiveInfinity;

    /// <summary>Gets or sets the maximum string length; zero uses the global limit.</summary>
    public int MaxLength { get; set; }
}
