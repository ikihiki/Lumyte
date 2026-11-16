using System.Runtime.CompilerServices;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;

namespace Lumyte.Graphics.Validation;

internal sealed class ValidationRenderEncoder : IRenderEncoder
{
    private readonly ValidationCommandBuffer _owner;
    private readonly IRenderEncoder _raw;
    private readonly ValidationEncoderChecks _checks;
    private IGraphicsPipeline? _pipeline;
    private IMeshPipeline? _meshPipeline;
    private GraphicsRenderStateDesc? _state;
    private bool _viewport;
    private bool _scissor;
    private bool _blend;
    private bool _stencil;
    private string _rootParameter = "arguments";

    private object? _indexBuffer;
    private ulong _indexOffset;
    private ulong _indexSize;
    private uint _indexStride;

    internal ValidationRenderEncoder(ValidationCommandBuffer owner, IRenderEncoder raw)
    {
        _owner = owner;
        _raw = raw;
        _checks = new(owner, this);
    }

    public void SetArgumentTable(IArgumentTable table) => _checks.SelectTable(table, _raw.SetArgumentTable);

    public void SetArgumentValidation(Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare) => _checks.SetValidation(prepare);

    public void SetPipeline(IGraphicsPipeline pipeline)
    {
        _checks.Check();
        IGraphicsPipeline native = _owner.Device.Unwrap(pipeline);
        using GraphicsValidation.RecordingTransaction tx = _owner.Device.Tracker.BeginRecording(_owner);
        _owner.Device.Tracker.Use(_owner, pipeline);
        _raw.SetPipeline(native);
        _pipeline = pipeline;
        _meshPipeline = null;
        _checks.SelectPipeline(pipeline);
        tx.Commit();
    }

    public void SetPipeline(IMeshPipeline pipeline)
    {
        _checks.Check();
        IMeshPipeline native = _owner.Device.Unwrap(pipeline);
        using GraphicsValidation.RecordingTransaction tx = _owner.Device.Tracker.BeginRecording(_owner);
        _owner.Device.Tracker.Use(_owner, pipeline);
        foreach ((IGraphicsShader shader, _) in ValidationEncoderChecks.MeshShaders(pipeline.Desc))
        {
            _owner.Device.Tracker.Use(_owner, shader);
        }

        _raw.SetPipeline(native);
        _meshPipeline = pipeline;
        _pipeline = null;
        _checks.SelectPipeline(pipeline);
        tx.Commit();
    }

    public void DrawMeshTasks(uint x, uint y = 1, uint z = 1)
    {
        _checks.Check();
        IMeshPipeline pipeline = _meshPipeline ?? throw new InvalidOperationException("Select a Mesh pipeline before Mesh execution.");
        _owner.Device.Unwrap(pipeline);
        ShaderWorkgroupLimits limits = pipeline.Desc.TaskShader is null
            ? _owner.Device.Caps.MeshShaderLimits?.Workgroup ?? throw new NotSupportedException("Mesh shaders are unavailable.")
            : _owner.Device.Caps.TaskShaderLimits?.Workgroup ?? throw new NotSupportedException("Task shaders are unavailable.");
        MeshShaderValidation.Dispatch(limits, x, y, z);
        if (_state is null || !_viewport || !_scissor || !_blend || !_stencil)
        {
            throw new InvalidOperationException("Set all required render state before Mesh execution.");
        }

        string? topology = MeshShaderValidation.Read(ValidationEncoderChecks.Target(_owner.Device, pipeline.Desc.MeshShader)).GetProperty("outputTopology").GetString();
        PrimitiveTopology expected = topology == "line" ? PrimitiveTopology.LineList : PrimitiveTopology.TriangleList;
        if (_state.Topology != expected || _state.StripIndexFormat is not null)
        {
            throw new InvalidOperationException("Mesh state must use its output list topology without strip restart.");
        }

        using GraphicsValidation.RecordingTransaction tx = _owner.Device.Tracker.BeginRecording(_owner);
        PipelineStage stages = PipelineStage.None;
        bool required = false;
        foreach ((IGraphicsShader shader, PipelineStage stage) in ValidationEncoderChecks.MeshShaders(pipeline.Desc))
        {
            _owner.Device.Unwrap(shader);
            _owner.Device.Tracker.Use(_owner, shader);
            ShaderTargetData target = ValidationEncoderChecks.Target(_owner.Device, shader);
            required |= ShaderDataLayout.HasRoot(target);
            if (ShaderDataLayout.HasRoot(target, _rootParameter))
            {
                stages |= stage;
            }
        }

        _checks.Execute(required, stages);
        _raw.DrawMeshTasks(x, y, z);
        tx.Commit();
    }

    public void SetArguments<T>(in T arguments)
        where T : struct, IShaderArguments
    {
        _checks.BeginArguments();
        try
        {
            IShaderReferenceMetadata? metadata = null;
            Action<IReadOnlyCollection<IShaderReference>>? admit = null;
            ValidationWriteContext context = _checks.Context(Diagnostic(T.RootParameter), (value, admission) =>
            {
                metadata = value;
                admit = admission;
            });
            var adapter = new ValidationArguments<T>(in arguments, context);
            _raw.SetArguments(in adapter);
            _checks.CommitRoot(metadata ?? throw new InvalidOperationException("Backend did not invoke the root writer."), admit);
            _rootParameter = T.RootParameter;
        }
        finally
        {
            _checks.EndArguments();
        }
    }

    public void SetArguments<T>(in T arguments, string rootParameter = "arguments", string? shaderTypeName = null)
        where T : unmanaged
    {
        _checks.BeginArguments();
        try
        {
            IShaderReferenceMetadata? metadata = null;
            Action<IReadOnlyCollection<IShaderReference>>? admit = null;
            _checks.PrepareUnmanaged(Diagnostic(rootParameter), Unsafe.SizeOf<T>(), ShaderTransfer.Fields<T>(), shaderTypeName ?? ShaderTransfer.TypeName<T>(), (value, admission) =>
            {
                metadata = value;
                admit = admission;
            });
            _raw.SetArguments(in arguments, rootParameter, shaderTypeName);
            _checks.CommitRoot(metadata!, admit);
            _rootParameter = rootParameter;
        }
        finally
        {
            _checks.EndArguments();
        }
    }

    public void End()
    {
        _checks.Check();
        _raw.End();
        _owner.EndPass(this);
    }

    public void SetRenderState(GraphicsRenderStateDesc state)
    {
        _checks.Check();
        _raw.SetRenderState(state);
        _state = state;
    }

    public void SetViewport(Viewport viewport)
    {
        _checks.Check();
        _raw.SetViewport(viewport);
        _viewport = true;
    }

    public void SetScissor(ScissorRect scissor)
    {
        _checks.Check();
        _raw.SetScissor(scissor);
        _scissor = true;
    }

    public void SetBlendConstant(BlendConstant value)
    {
        _checks.Check();
        _raw.SetBlendConstant(value);
        _blend = true;
    }

    public void SetStencilReference(uint reference)
    {
        _checks.Check();
        _raw.SetStencilReference(reference);
        _stencil = true;
    }

    public void SetIndexBuffer(BufferSlice<ushort> indices)
    {
        _checks.Check();
        BufferSlice<ushort> raw = _owner.Unwrap(indices);
        using GraphicsValidation.RecordingTransaction tx = _owner.Device.Tracker.BeginRecording(_owner);
        _owner.Device.Tracker.Use(_owner, indices.Buffer);
        _raw.SetIndexBuffer(raw);
        _indexBuffer = indices.Buffer;
        _indexOffset = indices.OffsetInBytes;
        _indexSize = indices.SizeInBytes;
        _indexStride = 2;
        tx.Commit();
    }

    public void SetIndexBuffer(BufferSlice<uint> indices)
    {
        _checks.Check();
        BufferSlice<uint> raw = _owner.Unwrap(indices);
        using GraphicsValidation.RecordingTransaction tx = _owner.Device.Tracker.BeginRecording(_owner);
        _owner.Device.Tracker.Use(_owner, indices.Buffer);
        _raw.SetIndexBuffer(raw);
        _indexBuffer = indices.Buffer;
        _indexOffset = indices.OffsetInBytes;
        _indexSize = indices.SizeInBytes;
        _indexStride = 4;
        tx.Commit();
    }

    public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
    {
        _checks.Check();
        using GraphicsValidation.RecordingTransaction tx = _owner.Device.Tracker.BeginRecording(_owner);
        PrepareExecution(vertexCount != 0 && instanceCount != 0);
        _raw.Draw(vertexCount, instanceCount, firstVertex, firstInstance);
        tx.Commit();
    }

    public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0)
    {
        _checks.Check();
        using GraphicsValidation.RecordingTransaction tx = _owner.Device.Tracker.BeginRecording(_owner);
        bool executes = indexCount != 0 && instanceCount != 0;
        PrepareExecution(executes);
        IndexAccess(firstIndex, indexCount, executes);
        _raw.DrawIndexed(indexCount, instanceCount, firstIndex, baseVertex, firstInstance);
        tx.Commit();
    }

    public void DrawIndirect(BufferSlice<DrawIndirectArguments> arguments)
    {
        _checks.Check();
        RequireGraphicsPipeline();
        BufferSlice<DrawIndirectArguments> native = _owner.Unwrap(arguments);
        using GraphicsValidation.RecordingTransaction tx = _owner.Device.Tracker.BeginRecording(_owner);
        PrepareExecution();
        Indirect(arguments);
        _raw.DrawIndirect(native);
        tx.Commit();
    }

    public void DrawIndexedIndirect(BufferSlice<DrawIndexedIndirectArguments> arguments)
    {
        _checks.Check();
        RequireGraphicsPipeline();
        BufferSlice<DrawIndexedIndirectArguments> native = _owner.Unwrap(arguments);
        using GraphicsValidation.RecordingTransaction tx = _owner.Device.Tracker.BeginRecording(_owner);
        PrepareExecution();
        IndexAccess(0, null);
        Indirect(arguments);
        _raw.DrawIndexedIndirect(native);
        tx.Commit();
    }

    private ShaderDataLayout Diagnostic(string root) => _meshPipeline is { } mesh ? ValidationEncoderChecks.MeshRoot(_owner.Device, mesh, root) : ValidationEncoderChecks.RenderRoot(_owner.Device, _pipeline!, root);

    private void RequireGraphicsPipeline()
    {
        if (_pipeline == null)
        {
            throw new InvalidOperationException("Select a traditional graphics pipeline before execution.");
        }
    }

    private void PrepareExecution(bool executes = true)
    {
        _checks.Check();
        if (_pipeline == null)
        {
            throw new InvalidOperationException("Select a pipeline before execution.");
        }

        bool required = ShaderDataLayout.HasRoot(ValidationEncoderChecks.Target(_owner.Device, _pipeline.Desc.VertexShader)) || (_pipeline.Desc.FragmentShader is { } fragment && ShaderDataLayout.HasRoot(ValidationEncoderChecks.Target(_owner.Device, fragment)));
        _checks.Execute(required, required ? ValidationEncoderChecks.RenderStages(_owner.Device, _pipeline, _rootParameter) : PipelineStage.None, executes);
    }

    private void Indirect<T>(BufferSlice<T> arguments)
        where T : unmanaged
        => _owner.Device.Tracker.Access(_owner, arguments.Buffer, new(PipelineStage.DrawIndirect, ResourceAccess.IndirectRead), arguments.OffsetInBytes, arguments.SizeInBytes);

    private void IndexAccess(uint firstIndex, uint? count, bool executes = true)
    {
        if (_indexBuffer == null)
        {
            throw new InvalidOperationException("Select an index buffer before indexed execution.");
        }

        ulong offset = checked((ulong)firstIndex * _indexStride);
        ulong size = count is { } n ? checked((ulong)n * _indexStride) : _indexSize;
        if (offset > _indexSize || size > _indexSize - offset)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        if (!executes)
        {
            return;
        }

        _owner.Device.Tracker.Access(_owner, _indexBuffer, new(PipelineStage.IndexInput, ResourceAccess.IndexRead), checked(_indexOffset + offset), size);
    }
}
