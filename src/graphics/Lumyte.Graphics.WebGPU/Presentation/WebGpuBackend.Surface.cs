using System.Runtime.InteropServices;
using P = Lumyte.Graphics.Portable;
using N = WebGpuSharp;
using F = WebGpuSharp.FFI;

namespace Lumyte.Graphics.WebGPU;

public sealed partial class WebGpuBackend
{
    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW")]
    private static partial nint GetPresentationModule(nint name);
    /// <summary>Creates a Dawn surface for a caller-owned Win32 window.</summary>
    public unsafe P.IPortableGpuSurface CreateWindowSurface(nint hwnd)
    {
        if (!OperatingSystem.IsWindows())
        { throw new PlatformNotSupportedException("This surface factory requires Win32."); }
        if (hwnd == 0)
        { throw new ArgumentException("A live HWND is required.", nameof(hwnd)); }
        lock (gate)
        {
            RequireAvailable();
            F.SurfaceSourceWindowsHWNDFFI source = new() { Chain = new() { SType = N.SType.SurfaceSourceWindowsHWND }, Hwnd = (void*)hwnd, Hinstance = (void*)GetPresentationModule(0) };
            F.SurfaceDescriptorFFI description = new() { NextInChain = &source.Chain };
            var surface = F.WebGPU_FFI.InstanceCreateSurface(instance, &description);
            if ((nuint)surface == 0)
            { throw new InvalidOperationException("WebGPU surface creation returned no object."); }
            try
            { return new WindowSurface(this, surface); }
            catch { F.WebGPU_FFI.SurfaceRelease(surface); throw; }
        }
    }
    private sealed class WindowSurface : P.IPortableGpuSurface
    {
        private readonly WebGpuBackend owner;
        private readonly F.SurfaceHandle surface;
        private readonly N.TextureFormat format;
        private readonly GpuFormat commonFormat;
        private uint width, height;
        private bool configured, disposed;
        private P.GpuSurfaceImage? acquired;
        internal unsafe WindowSurface(WebGpuBackend owner, F.SurfaceHandle surface)
        {
            this.owner = owner;
            this.surface = surface;
            F.SurfaceCapabilitiesFFI capabilities = default;
            if (F.WebGPU_FFI.SurfaceGetCapabilities(surface, owner.adapter, &capabilities) != N.Status.Success)
            { throw new NotSupportedException("The selected WebGPU adapter cannot present to this window."); }
            try
            {
                var formats = new ReadOnlySpan<N.TextureFormat>(capabilities.Formats, checked((int)capabilities.FormatCount));
                format = formats.Contains(N.TextureFormat.BGRA8Unorm) ? N.TextureFormat.BGRA8Unorm : N.TextureFormat.RGBA8Unorm;
                if (!formats.Contains(format))
                { throw new NotSupportedException("The surface has no SDR RGBA/BGRA format."); }
                commonFormat = format == N.TextureFormat.BGRA8Unorm ? GpuFormat.Bgra8Unorm : GpuFormat.Rgba8Unorm;
            }
            finally { F.WebGPU_FFI.SurfaceCapabilitiesFreeMembers(capabilities); }
        }
        public async ValueTask<P.GpuSurfaceImage> AcquireAsync(uint newWidth, uint newHeight, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TextureResource resource = Acquire(newWidth, newHeight);
            var diagnostics = await resource.Diagnostics.ConfigureAwait(false);
            if (diagnostics.Count != 0)
            {
                await DiscardAsync(acquired!).ConfigureAwait(false);
                throw new P.GpuOperationException("AcquireSurface", diagnostics);
            }
            return acquired!;
        }
        private unsafe TextureResource Acquire(uint newWidth, uint newHeight)
        {
            lock (owner.gate)
            {
                owner.RequireAvailable();
                ObjectDisposedException.ThrowIf(disposed, this);
                if (acquired is not null)
                { throw new InvalidOperationException("Return the previous surface image first."); }
                if (newWidth == 0 || newHeight == 0)
                { throw new ArgumentOutOfRangeException(nameof(newWidth), "Pause presentation while the window has zero extent."); }
                F.SurfaceTextureFFI current = default;
                Task<IReadOnlyList<P.GpuDiagnostic>> diagnostics;
                owner.PushScopes();
                try
                {
                    if (!configured || width != newWidth || height != newHeight)
                    { Configure(newWidth, newHeight); }
                    F.WebGPU_FFI.SurfaceGetCurrentTexture(surface, &current);
                    if (current.Status == N.SurfaceGetCurrentTextureStatus.Outdated)
                    {
                        if ((nuint)current.Texture != 0)
                        { F.WebGPU_FFI.TextureRelease(current.Texture); current.Texture = default; }
                        Configure(newWidth, newHeight);
                        F.WebGPU_FFI.SurfaceGetCurrentTexture(surface, &current);
                    }
                }
                finally { diagnostics = owner.PopScopes(); }
                if (current.Status is not (N.SurfaceGetCurrentTextureStatus.SuccessOptimal or N.SurfaceGetCurrentTextureStatus.SuccessSuboptimal) || (nuint)current.Texture == 0)
                {
                    if ((nuint)current.Texture != 0)
                    { F.WebGPU_FFI.TextureRelease(current.Texture); }
                    throw new InvalidOperationException($"WebGPU surface acquisition failed: {current.Status}.");
                }
                var description = new P.GpuTextureDescription(P.GpuTextureDimension.Texture2D, width, height, 1, 1, 1, 1, commonFormat, P.GpuTextureUsage.ColorAttachment);
                var resource = new TextureResource(owner, current.Texture, description, diagnostics);
                acquired = new(resource, description);
                return resource;
            }
        }
        private unsafe void Configure(uint newWidth, uint newHeight)
        {
            F.SurfaceConfigurationFFI configuration = new()
            {
                Device = owner.device,
                Format = format,
                Usage = N.TextureUsage.RenderAttachment,
                Width = newWidth,
                Height = newHeight,
                PresentMode = N.PresentMode.Fifo,
                AlphaMode = N.CompositeAlphaMode.Auto
            };
            F.WebGPU_FFI.SurfaceConfigure(surface, &configuration);
            width = newWidth;
            height = newHeight;
            configured = true;
        }
        public ValueTask PresentAsync(P.GpuSurfaceImage image)
        {
            lock (owner.gate)
            {
                Require(image);
                N.Status result = F.WebGPU_FFI.SurfacePresent(surface);
                Release(image);
                if (result != N.Status.Success)
                { throw new InvalidOperationException($"WebGPU SurfacePresent failed: {result}."); }
                return ValueTask.CompletedTask;
            }
        }
        public ValueTask DiscardAsync(P.GpuSurfaceImage image)
        {
            lock (owner.gate)
            { Require(image); Release(image); F.WebGPU_FFI.SurfaceUnconfigure(surface); configured = false; return ValueTask.CompletedTask; }
        }
        private void Release(P.GpuSurfaceImage image)
        {
            var resource = (TextureResource)image.Texture;
            resource.Destroyed = true;
            F.WebGPU_FFI.TextureRelease(resource.Handle);
            acquired = null;
        }
        private void Require(P.GpuSurfaceImage image)
        { owner.RequireAvailable(); ObjectDisposedException.ThrowIf(disposed, this); if (!ReferenceEquals(image, acquired)) { throw new ArgumentException("Image is not acquired from this surface.", nameof(image)); } }
        public ValueTask DisposeAsync()
        {
            lock (owner.gate)
            {
                if (disposed)
                { return ValueTask.CompletedTask; }
                if (acquired is not null)
                { throw new InvalidOperationException("Return the surface image before disposal."); }
                if (configured)
                { F.WebGPU_FFI.SurfaceUnconfigure(surface); }
                F.WebGPU_FFI.SurfaceRelease(surface);
                disposed = true;
                return ValueTask.CompletedTask;
            }
        }
    }
}
