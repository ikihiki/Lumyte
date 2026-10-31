using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Direct3D12;

internal sealed unsafe partial class Direct3D12GraphicsPipeline : IGraphicsPipeline
{
    private readonly Direct3D12Device _owner;
    private readonly Direct3D12Shader _vertex;
    private readonly Direct3D12Shader? _fragment;
    private bool _disposed;

    internal Direct3D12GraphicsPipeline(Direct3D12Device owner, GraphicsPipelineDesc desc)
    {
        (_owner, Desc) = (owner, desc);
        if (desc.VertexShader is not Direct3D12Shader vertex || !ReferenceEquals(vertex.Owner, owner) ||
            (desc.FragmentShader != null && (desc.FragmentShader is not Direct3D12Shader fragment || !ReferenceEquals(fragment.Owner, owner))))
        {
            throw new ArgumentException("Graphics shader belongs to another device.");
        }

        _vertex = vertex;
        _fragment = (Direct3D12Shader?)desc.FragmentShader;
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

    public GraphicsPipelineDesc Desc { get; }

    internal Direct3D12Device Owner => _owner;

    internal bool RequiresArguments { get; }

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
