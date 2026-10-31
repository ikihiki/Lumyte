using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Direct3D12;

internal sealed class Direct3D12Sampler(Direct3D12Device owner, SamplerDesc desc) : IGraphicsSampler
{
    private bool _disposed;

    public SamplerDesc Desc { get; } = desc;

    internal Direct3D12Device Owner => owner;

    public void Dispose() => _disposed = true;

    internal void ValidateAlive()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Owner.RequireReady();
    }
}
