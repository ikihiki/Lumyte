using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal sealed partial class BrowserGraphicsPipeline : IGraphicsPipeline
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
        FragmentOutputs = PipelineValidation.FragmentOutputs(FragmentData);
        Initialize();
        _vertex.RetainPipeline();
        _fragment?.RetainPipeline();
    }

    public GraphicsPipelineDesc Desc { get; }

    internal BrowserDevice Owner => _owner;

    internal ShaderTargetData VertexData { get; }

    internal ShaderTargetData? FragmentData { get; }

    internal IReadOnlyDictionary<uint, string>? FragmentOutputs { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        DisposeNative();
        _vertex.ReleasePipeline();
        _fragment?.ReleasePipeline();
        _disposed = true;
        _owner.ReleasePipeline();
    }

    internal void ValidateAlive()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _ = _vertex.Native;
        if (_fragment != null)
        {
            _ = _fragment.Native;
        }
    }
}
