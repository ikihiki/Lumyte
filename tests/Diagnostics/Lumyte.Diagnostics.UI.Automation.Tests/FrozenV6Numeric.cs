namespace Lumyte.Diagnostics.UI.Automation.Tests;

// Frozen v6 numeric contract, independent of production DTOs.
internal sealed record FrozenV6Numeric(double Minimum, double Maximum, double? Step, double? Value, double? Lower, double? Upper, bool IsValid);
