using Lumyte.Graphics.Native;

using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;

namespace Lumyte.Graphics.DirectX12;

public sealed unsafe partial class DirectX12Backend
{
    /// <summary>Creates a flip-model surface. The application keeps hwnd alive until the surface is disposed.</summary>
    public INativeGpuSurface CreateWindowSurface(nint hwnd, bool verticalSync = true)
    {
        VerifyAvailable();
        if (hwnd == 0)
        { throw new ArgumentException("A live HWND is required.", nameof(hwnd)); }
        return new WindowSurface(this, hwnd, verticalSync);
    }
    private sealed class WindowSurface(DirectX12Backend owner, nint hwnd, bool verticalSync) : INativeGpuSurface
    {
        // Keep the native library loaded while swapchain COM methods can still be called.
        private readonly DXGI api = new(new Silk.NET.Core.Contexts.DefaultNativeContext("dxgi.dll"));
        private ComPtr<IDXGISwapChain3> chain;
        private uint width, height;
        private NativeGpuSurfaceImage? acquired;
        private bool disposed;
        public ValueTask<NativeGpuSurfaceImage> AcquireAsync(uint newWidth, uint newHeight, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(disposed, this);
            owner.VerifyAvailable();
            if (acquired is not null)
            { throw new InvalidOperationException("Return the previous surface image first."); }
            if (newWidth == 0 || newHeight == 0)
            { throw new ArgumentOutOfRangeException(nameof(newWidth), "Pause presentation while the window has zero extent."); }
            if (chain.Handle == null)
            {
                ComPtr<IDXGIFactory2> factory = default;
                ComPtr<IDXGISwapChain1> initial = default;
                try
                {
                    Check(api.CreateDXGIFactory2(0, out factory), "CreateDXGIFactory2");
                    SwapChainDesc1 description = new()
                    {
                        Width = newWidth,
                        Height = newHeight,
                        Format = Format.FormatB8G8R8A8Unorm,
                        SampleDesc = new(1, 0),
                        BufferUsage = 0x20,
                        BufferCount = 3,
                        Scaling = Scaling.Stretch,
                        SwapEffect = SwapEffect.FlipDiscard,
                        AlphaMode = AlphaMode.Ignore
                    };
                    Check(factory.CreateSwapChainForHwnd((IUnknown*)owner.mainQueue.Handle, hwnd, &description, (SwapChainFullscreenDesc*)null, (IDXGIOutput*)null, initial.GetAddressOf()), "CreateSwapChainForHwnd");
                    Check(initial.QueryInterface(out chain), "QueryInterface(IDXGISwapChain3)");
                    Check(factory.MakeWindowAssociation(hwnd, 2), "MakeWindowAssociation");
                    width = newWidth;
                    height = newHeight;
                }
                catch { chain.Dispose(); chain = default; throw; }
                finally { initial.Dispose(); factory.Dispose(); }
            }
            else if (width != newWidth || height != newHeight)
            {
                owner.mainQueue.Collect();

                Check(chain.ResizeBuffers(3, newWidth, newHeight, Format.FormatB8G8R8A8Unorm, 0), "ResizeBuffers");
                width = newWidth;
                height = newHeight;
            }
            ComPtr<ID3D12Resource> resource = default;
            try
            {
                Check(chain.GetBuffer(chain.GetCurrentBackBufferIndex(), out resource), "GetBuffer");
                var description = new NativeGpuTextureDescription(NativeGpuTextureDimension.TwoD, width, height, 1, 1, 1, 1, GpuFormat.Bgra8Unorm, NativeGpuTextureUsage.ColorAttachment);
                acquired = new(new TextureRecord(owner, resource, description) { IsSurfaceImage = true }, description);
                return new(acquired);
            }
            catch { resource.Dispose(); throw; }
        }
        public ValueTask PresentAsync(NativeGpuSurfaceImage image) => new(Task.Run(() => Present(image)));
        private void Present(NativeGpuSurfaceImage image)
        {
            Require(image); // General maps to D3D12 COMMON/PRESENT; the graph has already restored it.
            int result = chain.Present(verticalSync ? 1u : 0u, 0);
            // Present adds queue use after the caller's rendering completion. Releasing the
            // image before this fence makes a following ResizeBuffers race that use.
            owner.mainQueue.DrainSurfaceUse();
            owner.DestroyTexture(image.Texture);
            acquired = null;
            owner.CheckDeviceResult(result, "IDXGISwapChain.Present");
        }
        public ValueTask DiscardAsync(NativeGpuSurfaceImage image) { Require(image); owner.DestroyTexture(image.Texture); acquired = null; return ValueTask.CompletedTask; }
        private void Require(NativeGpuSurfaceImage image)
        { ObjectDisposedException.ThrowIf(disposed, this); if (!ReferenceEquals(image, acquired)) { throw new ArgumentException("Image is not acquired from this surface.", nameof(image)); } }
        public ValueTask DisposeAsync()
        {
            if (disposed)
            { return ValueTask.CompletedTask; }
            if (acquired is not null)
            { throw new InvalidOperationException("Return the surface image before disposal."); }
            owner.mainQueue.Collect();
            chain.Dispose();
            chain = default;
            api.Dispose();
            disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
