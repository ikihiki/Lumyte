namespace Lumyte.Graphics;

/// <summary>
/// Selects a graphics backend supported by the composition root.
/// </summary>
public enum GraphicsBackend
{
    /// <summary>
    /// Uses the managed wgpu-native backend.
    /// </summary>
    Wgpu,
}
