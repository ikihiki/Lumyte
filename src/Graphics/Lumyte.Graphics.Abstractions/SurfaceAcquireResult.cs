namespace Lumyte.Graphics.Abstractions;

/// <summary>Returns a leased image only for Success or Suboptimal.</summary>
/// <param name="Status">The native acquisition outcome.</param>
/// <param name="Frame">The acquired frame, or null for an unsuccessful acquisition.</param>
public readonly record struct SurfaceAcquireResult(SurfaceStatus Status, IGraphicsSurfaceFrame? Frame);
