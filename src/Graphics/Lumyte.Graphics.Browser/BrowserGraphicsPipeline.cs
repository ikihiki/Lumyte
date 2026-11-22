using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal sealed partial class BrowserGraphicsPipeline : IGraphicsPipeline, IGraphicsNativeOrigin
{
    private readonly BrowserDevice _owner;
    private readonly BrowserShader _vertex;
    private readonly BrowserShader? _fragment;
    private bool _disposed;

    internal BrowserGraphicsPipeline(BrowserDevice owner, GraphicsPipelineDesc desc)
    {
        (_owner, Desc) = (owner, desc);
        if (desc.VertexShader is not BrowserShader vertex || !ReferenceEquals(vertex.Owner, owner) ||
            (desc.FragmentShader != null && (desc.FragmentShader is not BrowserShader fragment || !ReferenceEquals(fragment.Owner, owner))))
        {
            throw new ArgumentException("Graphics shader belongs to another device.");
        }

        _vertex = vertex;
        _fragment = (BrowserShader?)desc.FragmentShader;
        _ = vertex.Native;
        if (_fragment != null)
        {
            _ = _fragment.Native;
        }

        VertexData = PipelineValidation.Shader(vertex, owner.Caps.ShaderTarget, ShaderStage.Vertex, owner.Caps);
        FragmentData = _fragment == null ? null : PipelineValidation.Shader(_fragment, owner.Caps.ShaderTarget, ShaderStage.Fragment, owner.Caps);

        PipelineValidation.Program(desc, VertexData, FragmentData);
        RequiresArguments = ShaderDataLayout.HasRoot(VertexData) || (FragmentData != null && ShaderDataLayout.HasRoot(FragmentData));
        Initialize();
    }

    IGraphicDevice IGraphicsNativeOrigin.Device => Owner;

    object IGraphicsNativeOrigin.AllocationIdentity => this;

    public GraphicsPipelineDesc Desc { get; }

    internal BrowserDevice Owner => _owner;

    internal bool RequiresArguments { get; }

    internal ShaderTargetData VertexData { get; }

    internal ShaderTargetData? FragmentData { get; }

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
