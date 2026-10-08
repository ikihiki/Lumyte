namespace Lumyte.Graphics;

/// <summary>
/// Creates graphics devices through the common API.
/// </summary>
public static class Graphics
{
    /// <summary>
    /// Creates a device for the selected backend; unsupported choices are rejected.
    /// </summary>
    /// <param name="backend">The graphics backend to initialize.</param>
    /// <returns>The owned device for the selected backend.</returns>
    public static GraphicsDevice CreateDevice(GraphicsBackend backend = GraphicsBackend.Wgpu) => backend switch
    {
        GraphicsBackend.Wgpu => Wgpu.WgpuBackend.CreateDevice(),
        _ => throw new ArgumentOutOfRangeException(nameof(backend)),
    };
}
