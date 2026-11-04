namespace Lumyte.Diagnostics.UI.Automation.Contracts;

/// <summary>Finite surface logical bounds.</summary>
/// <param name="X">The X value.</param>
/// <param name="Y">The Y value.</param>
/// <param name="Width">The Width value.</param>
/// <param name="Height">The Height value.</param>
public sealed record UiRemoteRect(double X, double Y, double Width, double Height);
