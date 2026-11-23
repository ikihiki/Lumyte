using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Direct3D12;

internal sealed partial class Direct3D12CommandBuffer : IGraphicsNativeOrigin
{
    /// <inheritdoc/>
    IGraphicDevice IGraphicsNativeOrigin.Device => Owner;

    /// <inheritdoc/>
    object IGraphicsNativeOrigin.AllocationIdentity => this;

    /// <inheritdoc/>
    void IGraphicsNativeOrigin.ValidateOrigin()
    {
        RequireRecording();
        if (Owner.Status != GraphicsDeviceStatus.Ready)
        {
            throw new InvalidOperationException("Recording requires the actual originating device generation.");
        }
    }
}
