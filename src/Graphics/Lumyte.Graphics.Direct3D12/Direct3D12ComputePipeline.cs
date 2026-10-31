using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Direct3D12;

internal sealed unsafe partial class Direct3D12ComputePipeline : IGraphicsComputePipeline
{
    private readonly Direct3D12Device _owner;
    private readonly Direct3D12Shader _compute;
    private bool _disposed;

    internal Direct3D12ComputePipeline(Direct3D12Device owner, ComputePipelineDesc desc)
    {
        (_owner, Desc) = (owner, desc);
        if (desc.ComputeShader is not Direct3D12Shader shader || !ReferenceEquals(shader.Owner, owner))
        {
            throw new ArgumentException("Compute shader belongs to another device.");
        }

        _compute = shader;
        _ = shader.Native;
        Data = PipelineValidation.Shader(shader, owner.Caps.ShaderTarget, ShaderStage.Compute, owner.Caps);
        RequiresArguments = ShaderDataLayout.HasRoot(Data);
        Initialize();
    }

    public ComputePipelineDesc Desc { get; }

    internal Direct3D12Device Owner => _owner;

    internal bool RequiresArguments { get; }

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
