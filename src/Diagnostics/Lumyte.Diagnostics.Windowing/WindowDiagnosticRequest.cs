namespace Lumyte.Diagnostics.Windowing;

/// <summary>Identifies an accepted WindowSystem request.</summary>
/// <param name="System">The registered system ID.</param>
/// <param name="Window">The managed window ID.</param>
/// <param name="Request">The WindowSystem request ID.</param>
public sealed record WindowDiagnosticRequest(string System, string Window, long Request);
