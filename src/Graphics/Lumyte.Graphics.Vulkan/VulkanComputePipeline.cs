using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Vulkan;

internal sealed unsafe partial class VulkanComputePipeline : IGraphicsComputePipeline, IGraphicsNativeOrigin
{
    private readonly VulkanDevice _owner;
    private readonly VulkanShader _compute;
    private bool _disposed;

    internal VulkanComputePipeline(VulkanDevice owner, ComputePipelineDesc desc)
    {
        (_owner, Desc) = (owner, desc);
        if (desc.ComputeShader is not VulkanShader shader || !ReferenceEquals(shader.Owner, owner))
        {
            throw new ArgumentException("Compute shader belongs to another device.");
        }

        _compute = shader;
        _ = shader.Native;
        Data = PipelineValidation.Shader(shader, owner.Caps.ShaderTarget, ShaderStage.Compute, owner.Caps);
        RequiresArguments = ShaderDataLayout.HasRoot(Data);
        Initialize();
    }

    IGraphicDevice IGraphicsNativeOrigin.Device => Owner;

    object IGraphicsNativeOrigin.AllocationIdentity => this;

    public ComputePipelineDesc Desc { get; }

    internal VulkanDevice Owner => _owner;

    internal bool RequiresArguments { get; }

    internal ShaderTargetData Data { get; }

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

        DisposeNative();
        _disposed = true;
    }

    internal void ValidateAlive() => ObjectDisposedException.ThrowIf(_disposed, this);
}
