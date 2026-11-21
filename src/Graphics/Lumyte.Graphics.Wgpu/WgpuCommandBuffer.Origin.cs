using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Wgpu;

internal sealed partial class WgpuCommandBuffer : IGraphicsNativeOrigin
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
