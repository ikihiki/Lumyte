using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Vulkan;

internal sealed unsafe partial class VulkanGraphicsPipeline : IGraphicsPipeline
{
    private readonly VulkanDevice _owner;
    private readonly VulkanShader _vertex;
    private readonly VulkanShader? _fragment;
    private bool _disposed;

    internal VulkanGraphicsPipeline(VulkanDevice owner, GraphicsPipelineDesc desc)
    {
        (_owner, Desc) = (owner, desc);
        if (desc.VertexShader is not VulkanShader vertex || !ReferenceEquals(vertex.Owner, owner) ||
            (desc.FragmentShader != null && (desc.FragmentShader is not VulkanShader fragment || !ReferenceEquals(fragment.Owner, owner))))
        {
            throw new ArgumentException("Graphics shader belongs to another device.");
        }

        _vertex = vertex;
        _fragment = (VulkanShader?)desc.FragmentShader;
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

    internal VulkanDevice Owner => _owner;

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
