using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal sealed partial class BrowserComputePipeline : IGraphicsComputePipeline, IGraphicsNativeOrigin
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
        RequiresArguments = ShaderDataLayout.HasRoot(Data);
        Initialize();
    }

    IGraphicDevice IGraphicsNativeOrigin.Device => Owner;

    object IGraphicsNativeOrigin.AllocationIdentity => this;

    public ComputePipelineDesc Desc { get; }

    internal BrowserDevice Owner => _owner;

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
