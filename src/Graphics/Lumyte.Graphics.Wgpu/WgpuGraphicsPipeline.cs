using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Wgpu;

internal sealed unsafe partial class WgpuGraphicsPipeline : IGraphicsPipeline
{
    private readonly WgpuDevice _owner;
    private readonly WgpuShader _vertex;
    private readonly WgpuShader? _fragment;
    private bool _disposed;

    internal WgpuGraphicsPipeline(WgpuDevice owner, GraphicsPipelineDesc desc)
    {
        (_owner, Desc) = (owner, desc);
        if (desc.VertexShader is not WgpuShader vertex || !ReferenceEquals(vertex.Owner, owner) ||
            (desc.FragmentShader != null && (desc.FragmentShader is not WgpuShader fragment || !ReferenceEquals(fragment.Owner, owner))))
        {
            throw new ArgumentException("Graphics shader belongs to another device.");
        }

        _vertex = vertex;
        _fragment = (WgpuShader?)desc.FragmentShader;
        _ = vertex.Native;
        if (_fragment != null)
        {
            _ = _fragment.Native;
        }

        VertexData = PipelineValidation.Shader(vertex, owner.Caps.ShaderTarget, ShaderStage.Vertex, owner.Caps);
        FragmentData = _fragment == null ? null : PipelineValidation.Shader(_fragment, owner.Caps.ShaderTarget, ShaderStage.Fragment, owner.Caps);
        PipelineValidation.Program(desc, VertexData, FragmentData);
        Initialize();
    }

    public GraphicsPipelineDesc Desc { get; }

    internal WgpuDevice Owner => _owner;

    internal ShaderTargetData VertexData { get; }

    internal ShaderTargetData? FragmentData { get; }

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
