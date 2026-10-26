namespace Lumyte.Graphics.Abstractions;

/// <summary>Preserves a backend's device loss diagnosis.</summary>
/// <param name="Reason">The backend reason or result code.</param>
/// <param name="Message">The diagnostic message.</param>
public sealed record GraphicsDeviceLoss(string Reason, string Message);
