namespace Lumyte.Diagnostics;

/// <summary>A scalar schema field.</summary>
/// <param name="Id">The Id argument.</param>
/// <param name="Kind">The Kind argument.</param>
/// <param name="Minimum">The Minimum argument.</param>
/// <param name="Maximum">The Maximum argument.</param>
/// <param name="MaxLength">The MaxLength argument.</param>
public sealed record DiagnosticField(
    string Id, DiagnosticValueKind Kind, double? Minimum = null, double? Maximum = null, int? MaxLength = null);
