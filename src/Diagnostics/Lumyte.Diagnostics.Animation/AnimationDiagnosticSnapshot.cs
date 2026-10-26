namespace Lumyte.Diagnostics.Animation;

/// <summary>A detached scalar snapshot of one registered animation target.</summary>
/// <param name="Id">The stable target ID.</param>
/// <param name="Kind">The target kind.</param>
/// <param name="Status">The playback or machine status.</param>
/// <param name="State">The formatted animation state, or an empty string for playback.</param>
/// <param name="PositionTicks">The current local position.</param>
/// <param name="Speed">The current playback speed.</param>
public sealed record AnimationDiagnosticSnapshot(
    string Id,
    string Kind,
    string Status,
    string State,
    long PositionTicks,
    double Speed);
