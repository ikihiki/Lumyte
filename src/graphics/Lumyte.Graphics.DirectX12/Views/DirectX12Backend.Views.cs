using Lumyte.Graphics.Native;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;

namespace Lumyte.Graphics.DirectX12;

public sealed unsafe partial class DirectX12Backend
{
    public NativeGpuRenderViewHandle CreateRenderView(NativeGpuTextureView view,
        NativeGpuRenderViewFlags flags = NativeGpuRenderViewFlags.None)
    {
        VerifyAvailable();
        TextureRecord texture = RequireTexture(view.Texture);
        bool color = view.Aspect == NativeGpuTextureAspect.Color;
        RenderTargetViewDesc rtv = default;
        DepthStencilViewDesc dsv = default;
        if (color) { rtv = RenderTargetDescription(view, texture.Description, flags); }
        else { dsv = DepthStencilDescription(view, texture.Description.SampleCount, flags); }
        var description = new DescriptorHeapDesc(color ? DescriptorHeapType.Rtv : DescriptorHeapType.Dsv, 1);
        ComPtr<ID3D12DescriptorHeap> heap = default;
        try
        {
            Check(device.CreateDescriptorHeap(in description, out heap), "CreateDescriptorHeap(render view)");
            CpuDescriptorHandle handle = heap.GetCPUDescriptorHandleForHeapStart();
            if (color) { device.CreateRenderTargetView(texture.Resource, in rtv, handle); }
            else { device.CreateDepthStencilView(texture.Resource, in dsv, handle); }
            return new RenderViewRecord(this, heap, view, flags);
        }
        catch { heap.Dispose(); throw; }
    }

    public void DestroyRenderView(NativeGpuRenderViewHandle view)
    {
        VerifyNotDisposed();
        ArgumentNullException.ThrowIfNull(view);
        if (view is not RenderViewRecord record || !ReferenceEquals(record.Owner, this))
        {
            throw new ArgumentException("The render view belongs to another device.", nameof(view));
        }
        ObjectDisposedException.ThrowIf(record.Disposed, view);
        record.Disposed = true;
        record.Heap.Dispose();
    }

    private sealed class RenderViewRecord(DirectX12Backend owner, ComPtr<ID3D12DescriptorHeap> heap,
        NativeGpuTextureView view, NativeGpuRenderViewFlags flags) : NativeGpuRenderViewHandle(flags)
    {
        public DirectX12Backend Owner { get; } = owner;
        public ComPtr<ID3D12DescriptorHeap> Heap = heap;
        public NativeGpuTextureView View { get; } = view;
        public bool Disposed;
    }
}
