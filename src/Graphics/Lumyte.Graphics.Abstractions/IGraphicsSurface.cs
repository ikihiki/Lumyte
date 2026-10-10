namespace Lumyte.Graphics.Abstractions;

/// <summary>Owns the graphics connection to a borrowed presentation target.</summary>
/// <remarks>The caller keeps the native target alive and manages all access synchronization.</remarks>
public interface IGraphicsSurface : IDisposable
{
    /// <summary>Queries current device-specific target capabilities without configuring it.</summary>
    /// <returns>The independently owned capability snapshot.</returns>
    SurfaceCapabilities GetCapabilities();

    /// <summary>Creates the surface's sole active swapchain.</summary>
    /// <param name="desc">The exact requested configuration.</param>
    /// <returns>The owned backend swapchain.</returns>
    IGraphicsSwapchain CreateSwapchain(SwapchainDesc desc);
}
