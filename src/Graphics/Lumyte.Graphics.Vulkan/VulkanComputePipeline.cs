using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Vulkan;

internal sealed unsafe partial class VulkanComputePipeline : IGraphicsComputePipeline
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
        Initialize();
        _compute.RetainPipeline();
    }

    public ComputePipelineDesc Desc { get; }

    internal VulkanDevice Owner => _owner;

    internal ShaderTargetData Data { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        DisposeNative();
        _compute.ReleasePipeline();
        _disposed = true;
        _owner.ReleasePipeline();
    }

    internal void ValidateAlive()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _ = _compute.Native;
    }
}
