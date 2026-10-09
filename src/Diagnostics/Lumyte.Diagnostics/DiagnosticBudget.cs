namespace Lumyte.Diagnostics;

/// <summary>The work permitted in one frame.</summary>
/// <param name="MaxDuration">The MaxDuration argument.</param>
/// <param name="MaxCommands">The MaxCommands argument.</param>
public sealed record DiagnosticBudget(TimeSpan MaxDuration, int MaxCommands);
