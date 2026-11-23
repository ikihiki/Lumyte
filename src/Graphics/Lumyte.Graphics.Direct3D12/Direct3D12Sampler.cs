using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Direct3D12;

internal sealed class Direct3D12Sampler(Direct3D12Device owner, SamplerDesc desc) : IGraphicsSampler, IGraphicsNativeOrigin
{
    private bool _disposed;

    IGraphicDevice IGraphicsNativeOrigin.Device => Owner;

    object IGraphicsNativeOrigin.AllocationIdentity => throw new NotSupportedException(
        "Direct3D 12 samplers have no standalone native allocation; descriptors are materialized by shader bindings.");

    public SamplerDesc Desc { get; } = desc;

    internal Direct3D12Device Owner => owner;

    void IGraphicsNativeOrigin.ValidateOrigin()
    {
        ObjectDisposedException.ThrowIf(Owner.Status == GraphicsDeviceStatus.Disposed, Owner);
        if (Owner.Status == GraphicsDeviceStatus.Lost)
        {
            throw new GraphicsDeviceLostException(Owner.Loss!);
        }

        ValidateAlive();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
    }

    internal void ValidateAlive()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Owner.RequireReady();
    }
}
