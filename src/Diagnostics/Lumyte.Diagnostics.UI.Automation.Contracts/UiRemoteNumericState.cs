namespace Lumyte.Diagnostics.UI.Automation.Contracts;

/// <summary>Exact numeric metadata, independent of normalized progress Value.</summary>
/// <param name="Minimum">The inclusive minimum.</param>
/// <param name="Maximum">The inclusive maximum.</param>
/// <param name="Step">The slider step, or null for an editor.</param>
/// <param name="Value">The accepted scalar, or null for empty or range.</param>
/// <param name="Lower">The lower range endpoint, otherwise null.</param>
/// <param name="Upper">The upper range endpoint, otherwise null.</param>
/// <param name="IsValid">The buffer validity, without disclosing its text.</param>
public sealed record UiRemoteNumericState(double Minimum, double Maximum, double? Step, double? Value, double? Lower, double? Upper, bool IsValid);
