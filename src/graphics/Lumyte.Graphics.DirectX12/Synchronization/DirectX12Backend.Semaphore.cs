using Lumyte.Graphics.Native;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;

namespace Lumyte.Graphics.DirectX12;

public sealed unsafe partial class DirectX12Backend
{
    private sealed class NativeSemaphore(NativeQueue owner, ComPtr<ID3D12Fence> fence) : NativeGpuSemaphore
    {
        public NativeQueue Owner { get; } = owner;
        public ComPtr<ID3D12Fence> Fence = fence;
        public bool Disposed { get; private set; }

        public override void Dispose()
        {
            if (Disposed) { return; }
            Disposed = true;
            Fence.Dispose();
        }
    }
}
