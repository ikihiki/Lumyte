namespace Lumyte.Graphics.Wgpu;

/// <summary>
/// Creates the managed wgpu implementation of the common graphics device.
/// </summary>
public static class WgpuBackend
{
    /// <summary>
    /// Creates a wgpu-native device and its common API owner.
    /// </summary>
    /// <returns>The owned device for the selected backend.</returns>
    public static GraphicsDevice CreateDevice() => new(new WgpuDriver(WgpuDevice.Create()));
}
