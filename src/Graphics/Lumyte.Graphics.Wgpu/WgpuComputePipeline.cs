using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Wgpu;

internal sealed unsafe partial class WgpuComputePipeline : IGraphicsComputePipeline
{
    private readonly WgpuDevice _owner;
    private readonly WgpuShader _compute;
    private bool _disposed;

    internal WgpuComputePipeline(WgpuDevice owner, ComputePipelineDesc desc)
    {
        (_owner, Desc) = (owner, desc);
        if (desc.ComputeShader is not WgpuShader shader || !ReferenceEquals(shader.Owner, owner))
        {
            throw new ArgumentException("Compute shader belongs to another device.");
        }

        _compute = shader;
        _ = shader.Native;
        Data = PipelineValidation.Shader(shader, owner.Caps.ShaderTarget, ShaderStage.Compute, owner.Caps);
        Initialize();
    }

    public ComputePipelineDesc Desc { get; }

    internal WgpuDevice Owner => _owner;

    internal ShaderTargetData Data { get; }

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
