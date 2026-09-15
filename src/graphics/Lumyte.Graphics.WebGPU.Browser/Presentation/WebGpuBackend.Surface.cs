using System.Runtime.InteropServices.JavaScript;

using P = Lumyte.Graphics.Portable;

namespace Lumyte.Graphics.WebGPU.Browser;

public sealed partial class WebGpuBackend
{
    /// <summary>Creates a presentation connection to an existing HTML canvas. The caller keeps the canvas and runtime alive.</summary>
    public P.IPortableGpuSurface CreateCanvasSurface(string elementId)
    {
        RequireAvailable();
        ArgumentException.ThrowIfNullOrWhiteSpace(elementId);
        return new CanvasSurface(this, BrowserInterop.CreateCanvasSurface(device, elementId));
    }
    private sealed class CanvasSurface(WebGpuBackend owner, JSObject context) : P.IPortableGpuSurface
    {
        private P.GpuSurfaceImage? acquired;
        private bool disposed;
        public ValueTask<P.GpuSurfaceImage> AcquireAsync(uint width, uint height, CancellationToken cancellationToken = default)
        {
            owner.RequireAvailable();
            ObjectDisposedException.ThrowIf(disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (acquired is not null)
            { throw new InvalidOperationException("Return the previous canvas image first."); }
            if (width == 0 || height == 0)
            { throw new ArgumentOutOfRangeException(nameof(width), "Pause presentation while canvas extent is zero."); }
            BrowserInterop.ResizeCanvasSurface(context, checked((int)width), checked((int)height));
            var description = new P.GpuTextureDescription(P.GpuTextureDimension.Texture2D, width, height, 1, 1, 1, 1, GpuFormat.Rgba8Unorm,
                P.GpuTextureUsage.ColorAttachment | P.GpuTextureUsage.CopySource | P.GpuTextureUsage.CopyDestination);
            // Canvas textures can expire across asynchronous graph preparation. Only the final JS call acquires one.
            acquired = new(owner.CreateTexture(description), description);
            return new(acquired);
        }
        public async ValueTask PresentAsync(P.GpuSurfaceImage image)
        {
            Require(image);
            var texture = (TextureResource)image.Texture;
            await BrowserInterop.PresentCanvasSurfaceAsync(owner.device, context, texture.Handle);
            owner.DestroyTexture(image.Texture);
            acquired = null;
        }
        public ValueTask DiscardAsync(P.GpuSurfaceImage image)
        { Require(image); owner.DestroyTexture(image.Texture); acquired = null; return ValueTask.CompletedTask; }
        private void Require(P.GpuSurfaceImage image)
        { owner.RequireAvailable(); ObjectDisposedException.ThrowIf(disposed, this); if (!ReferenceEquals(image, acquired)) { throw new ArgumentException("Image is not acquired from this canvas.", nameof(image)); } }
        public ValueTask DisposeAsync()
        {
            if (disposed)
            { return ValueTask.CompletedTask; }
            if (acquired is not null)
            { throw new InvalidOperationException("Return the canvas image before disposal."); }
            BrowserInterop.UnconfigureCanvasSurface(context);
            context.Dispose();
            disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
