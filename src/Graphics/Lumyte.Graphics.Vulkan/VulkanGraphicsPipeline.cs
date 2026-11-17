using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Vulkan;

internal sealed unsafe partial class VulkanGraphicsPipeline : IGraphicsPipeline
{
    private readonly VulkanDevice _owner;
    private readonly VulkanShader _vertex;
    private readonly VulkanShader? _fragment;
    private readonly VulkanShader? _task;
    private readonly bool _mesh;
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
        RequiresArguments = ShaderDataLayout.HasRoot(VertexData) || (FragmentData != null && ShaderDataLayout.HasRoot(FragmentData));
        Initialize();
        if (desc.FragmentShader != null)
        {
        }
    }

    internal VulkanGraphicsPipeline(VulkanDevice owner, MeshPipelineDesc desc)
    {
        (_owner, _mesh) = (owner, true);
        if ((owner.Caps.Features & GraphicsFeatures.MeshShader) == 0 ||
            (desc.TaskShader != null && (owner.Caps.Features & GraphicsFeatures.TaskShader) == 0))
        {
            throw new NotSupportedException("Mesh/Task shaders are not enabled on this device.");
        }

        _vertex = MeshShader(desc.MeshShader);
        _task = desc.TaskShader == null ? null : MeshShader(desc.TaskShader);
        _fragment = desc.FragmentShader == null ? null : MeshShader(desc.FragmentShader);
        VertexData = PipelineValidation.Shader(_vertex, owner.Caps.ShaderTarget, ShaderStage.Mesh, owner.Caps);
        TaskData = _task == null ? null : PipelineValidation.Shader(_task, owner.Caps.ShaderTarget, ShaderStage.Task, owner.Caps);
        FragmentData = _fragment == null ? null : PipelineValidation.Shader(_fragment, owner.Caps.ShaderTarget, ShaderStage.Fragment, owner.Caps);
        MeshTopology = VulkanMeshValidation.Program(owner, desc, VertexData, TaskData, FragmentData);
        Desc = new() { VertexShader = desc.MeshShader, FragmentShader = desc.FragmentShader, AlphaToCoverageEnable = desc.AlphaToCoverageEnable, Optimization = desc.Optimization };
        RequiresArguments = ShaderDataLayout.HasRoot(VertexData) || (TaskData != null && ShaderDataLayout.HasRoot(TaskData)) || (FragmentData != null && ShaderDataLayout.HasRoot(FragmentData));
        Initialize();

        VulkanShader MeshShader(IGraphicsShader shader)
        {
            if (shader is not VulkanShader native || !ReferenceEquals(native.Owner, owner))
            {
                throw new ArgumentException("Shader belongs to another device.");
            }

            _ = native.Native;
            return native;
        }
    }

    public GraphicsPipelineDesc Desc { get; }

    internal ShaderTargetData? TaskData { get; }

    internal PrimitiveTopology MeshTopology { get; }

    internal bool IsMesh => _mesh;

    internal VulkanDevice Owner => _owner;

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

    internal void ValidateAlive()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_mesh)
        {
            _ = _vertex.Native;
            if (_task != null)
            {
                _ = _task.Native;
            }

            if (_fragment != null)
            {
                _ = _fragment.Native;
            }
        }
    }
}
