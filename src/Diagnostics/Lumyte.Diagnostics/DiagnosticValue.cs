namespace Lumyte.Diagnostics;

/// <summary>A typed scalar without boxed numeric values.</summary>
/// <param name="Kind">The Kind argument.</param>
/// <param name="Boolean">The Boolean argument.</param>
/// <param name="Int64">The Int64 argument.</param>
/// <param name="Double">The Double argument.</param>
/// <param name="String">The String argument.</param>
public readonly record struct DiagnosticValue(
    DiagnosticValueKind Kind, bool Boolean = false, long Int64 = 0, double Double = 0, string? String = null)
{
    /// <summary>Creates a boolean scalar.</summary>
    /// <param name="value">The value argument.</param>
    /// <returns>The computed result.</returns>
    public static DiagnosticValue From(bool value) => new(DiagnosticValueKind.Boolean, Boolean: value);

    /// <summary>Creates an integer scalar.</summary>
    /// <param name="value">The value argument.</param>
    /// <returns>The computed result.</returns>
    public static DiagnosticValue From(long value) => new(DiagnosticValueKind.Int64, Int64: value);

    /// <summary>Creates a floating point scalar.</summary>
    /// <param name="value">The value argument.</param>
    /// <returns>The computed result.</returns>
    public static DiagnosticValue From(double value) => new(DiagnosticValueKind.Double, Double: value);

    /// <summary>Creates a string scalar.</summary>
    /// <param name="value">The value argument.</param>
    /// <returns>The computed result.</returns>
    public static DiagnosticValue From(string value) => new(DiagnosticValueKind.String, String: value);
}
