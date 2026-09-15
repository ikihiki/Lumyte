namespace Lumyte.Graphics.Native;

/// <summary>Optional backend presentation extension. The caller keeps the window alive until disposal.</summary>
/// <remarks>Only one image may be acquired at a time. Present and Discard require GPU use to have ended.
/// Acquired images enter and leave graph rendering in General layout. Surface ownership includes its images.</remarks>
public interface INativeGpuSurface : IAsyncDisposable
{
    ValueTask<NativeGpuSurfaceImage> AcquireAsync(uint width, uint height, CancellationToken cancellationToken = default);
    ValueTask PresentAsync(NativeGpuSurfaceImage image);
    ValueTask DiscardAsync(NativeGpuSurfaceImage image);
}
public sealed record NativeGpuSurfaceImage(NativeGpuTextureHandle Texture, NativeGpuTextureDescription Description);
