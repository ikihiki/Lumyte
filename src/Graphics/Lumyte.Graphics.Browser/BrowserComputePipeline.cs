using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal sealed partial class BrowserComputePipeline : IGraphicsComputePipeline
{
    private readonly BrowserDevice _owner;
    private readonly BrowserShader _compute;
    private bool _disposed;

    internal BrowserComputePipeline(BrowserDevice owner, ComputePipelineDesc desc)
    {
        (_owner, Desc) = (owner, desc);
        if (desc.ComputeShader is not BrowserShader shader || !ReferenceEquals(shader.Owner, owner))
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

    internal BrowserDevice Owner => _owner;

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
