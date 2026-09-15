namespace Lumyte.Graphics.Portable;

/// <summary>Optional native-window or canvas presentation extension. The caller owns the window/canvas lifetime.</summary>
/// <remarks>One image may be acquired at a time. All GPU use must end before Present or Discard. Images belong to the surface.</remarks>
public interface IPortableGpuSurface : IAsyncDisposable
{
    ValueTask<GpuSurfaceImage> AcquireAsync(uint width, uint height, CancellationToken cancellationToken = default);
    ValueTask PresentAsync(GpuSurfaceImage image);
    ValueTask DiscardAsync(GpuSurfaceImage image);
}
public sealed record GpuSurfaceImage(GpuTextureHandle Texture, GpuTextureDescription Description);
