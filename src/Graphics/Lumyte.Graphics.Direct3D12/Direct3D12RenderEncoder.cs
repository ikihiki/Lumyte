using System.Runtime.CompilerServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Direct3D12;

internal sealed unsafe class Direct3D12RenderEncoder(Direct3D12CommandBuffer owner, RenderColorAttachmentDesc[] attachments, RenderDepthStencilAttachmentDesc? depth) : IRenderEncoder
{
    private readonly TextureFormat[] _formats = attachments.Select(a => a.View.Info.Format).ToArray();
    private readonly (uint Width, uint Height) _size = (attachments.FirstOrDefault()?.View ?? depth!.View).Texture.GetMipSize((attachments.FirstOrDefault()?.View ?? depth!.View).Info.BaseMipLevel);
    private readonly TextureFormat? _depthFormat = depth?.View.Info.Format;
    private Direct3D12GraphicsPipeline? _pipeline;
    private Direct3D12MeshPipeline? _meshPipeline;
    private RenderStateSnapshot? _state;
    private IndexFormat? _indexFormat;
    private ulong _indexCount;
    private bool _viewport;
    private bool _scissor;
    private bool _blend;
    private bool _stencil;

    private ShaderRootArguments? _arguments;
    private object? _argumentProgram;
    private bool _settingArguments;
    private Direct3D12ArgumentTable? _table;
    private Direct3D12ArgumentTable? _argumentTableHint;

    private object? SelectedProgram => (object?)_meshPipeline ?? _pipeline;

    private bool RequiresArguments => _meshPipeline?.RequiresArguments ?? _pipeline?.RequiresArguments ?? false;

    public void SetArgumentTable(IArgumentTable table)
    {
        // Graph preparation may bind its already-declared table while arguments are encoded.

        // Graph admission legitimately autobinds its table here; it does not change root bytes.
        owner.ValidatePass(this);
        ArgumentNullException.ThrowIfNull(table);
        if (table is not Direct3D12ArgumentTable selected || !selected.BelongsTo(owner.Owner))
        {
            throw new ArgumentException("Argument table belongs to another device.", nameof(table));
        }

        selected.ThrowIfDisposed();
        _table = selected;
    }

    public void SetArgumentValidation(Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare)
    {
        ThrowIfSettingArguments();
        owner.ValidatePass(this);
        if (prepare != null)
        {
            throw new NotSupportedException("Use a ValidationDevice for argument validation.");
        }
    }

    public void SetArguments<T>(in T value)
        where T : struct, IShaderArguments
    {
        ValidateRootSelection();
        object pipeline = SelectedProgram!;
        _settingArguments = true;
        try
        {
            int words = Direct3D12ShaderTransfer.ValidateRootSize(T.TransferSize);
            Span<byte> payload = stackalloc byte[words * 4];
            payload.Clear();
            (ShaderRootArguments? root, Direct3D12ArgumentTable? tableHint) = Direct3D12ShaderTransfer.CreateRoot(owner.Owner, in value, payload);
            SelectRoot(root, payload, pipeline, tableHint);
        }
        finally
        {
            _settingArguments = false;
        }
    }

    public void SetArguments<T>(in T value, string rootParameter = "arguments", string? shaderTypeName = null)
        where T : unmanaged
    {
        ValidateRootSelection();
        object pipeline = SelectedProgram!;
        _settingArguments = true;
        try
        {
            int words = Direct3D12ShaderTransfer.ValidateRootSize(Unsafe.SizeOf<T>());
            Span<byte> payload = stackalloc byte[words * 4];
            payload.Clear();
            ShaderRootArguments root = Direct3D12ShaderTransfer.CreateUnmanaged(owner.Owner, in value, rootParameter, shaderTypeName, payload);
            SelectRoot(root, payload, pipeline, null);
        }
        finally
        {
            _settingArguments = false;
        }
    }

    public void SetPipeline(IGraphicsPipeline pipeline)
    {
        ThrowIfSettingArguments();
        owner.ValidatePass(this);
        ArgumentNullException.ThrowIfNull(pipeline);
        if (pipeline is not Direct3D12GraphicsPipeline program || !ReferenceEquals(program.Owner, owner.Owner))
        {
            throw new ArgumentException("Pipeline belongs to another device.");
        }

        program.ValidateAlive();
        _pipeline = program;
        _meshPipeline = null;
    }

    public void SetPipeline(IMeshPipeline pipeline)
    {
        ThrowIfSettingArguments();
        owner.ValidatePass(this);
        ArgumentNullException.ThrowIfNull(pipeline);
        if (pipeline is not Direct3D12MeshPipeline program || !ReferenceEquals(program.Owner, owner.Owner))
        {
            throw new ArgumentException("Pipeline belongs to another device.");
        }

        program.ValidateAlive();
        _meshPipeline = program;
        _pipeline = null;
    }

    public void DrawMeshTasks(uint x, uint y = 1, uint z = 1)
    {
        ThrowIfSettingArguments();
        owner.ValidatePass(this);
        if (_meshPipeline == null)
        {
            throw new InvalidOperationException("Select a mesh pipeline before mesh execution.");
        }

        MeshShaderValidation.Dispatch(_meshPipeline.DispatchLimits, x, y, z);
        PrepareDraw(false, true);
        owner.Owner.Check(NativeMethods.DispatchMesh(owner.Native, x, y, z));
    }

    public void SetRenderState(GraphicsRenderStateDesc state)
    {
        ThrowIfSettingArguments();
        owner.ValidatePass(this);
        RenderStateSnapshot snapshot = PipelineValidation.State(state, owner.Owner.Caps);
        _state = snapshot;
    }

    public void SetViewport(Viewport viewport)
    {
        ThrowIfSettingArguments();
        owner.ValidatePass(this);
        PipelineValidation.Viewport(viewport);
        if (viewport.X < 0 || viewport.Y < 0 || viewport.Width > _size.Width - viewport.X || viewport.Height > _size.Height - viewport.Y)
        {
            throw new ArgumentException("Viewport is outside the pass extent.");
        }

        NativeMethods.Viewport(owner.Native, viewport.X, viewport.Y, viewport.Width, viewport.Height, viewport.MinDepth, viewport.MaxDepth);
        _viewport = true;
    }

    public void SetScissor(ScissorRect scissor)
    {
        ThrowIfSettingArguments();
        owner.ValidatePass(this);
        PipelineValidation.Scissor(scissor, _size.Width, _size.Height);
        NativeMethods.Scissor(owner.Native, checked((int)scissor.X), checked((int)scissor.Y), checked((int)scissor.Width), checked((int)scissor.Height));
        _scissor = true;
    }

    public void SetBlendConstant(BlendConstant value)
    {
        ThrowIfSettingArguments();
        owner.ValidatePass(this);
        if (!float.IsFinite(value.Red) || !float.IsFinite(value.Green) || !float.IsFinite(value.Blue) || !float.IsFinite(value.Alpha))
        {
            throw new ArgumentException("Blend constants must be finite.");
        }

        NativeMethods.Blend(owner.Native, value.Red, value.Green, value.Blue, value.Alpha);
        _blend = true;
    }

    public void SetStencilReference(uint reference)
    {
        ThrowIfSettingArguments();
        owner.ValidatePass(this);
        if (reference > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(reference));
        }

        NativeMethods.Stencil(owner.Native, reference);
        _stencil = true;
    }

    public void SetIndexBuffer(BufferSlice<ushort> indices) => SetIndices(indices, IndexFormat.Uint16);

    public void SetIndexBuffer(BufferSlice<uint> indices) => SetIndices(indices, IndexFormat.Uint32);

    public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
    {
        ThrowIfSettingArguments();
        RequireTraditionalProgram();
        _ = checked(firstVertex + vertexCount);
        _ = checked(firstInstance + instanceCount);
        PrepareDraw(false);
        NativeMethods.Draw(owner.Native, vertexCount, instanceCount, firstVertex, 0, firstInstance, 0);
    }

    public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0)
    {
        ThrowIfSettingArguments();
        RequireTraditionalProgram();
        owner.ValidatePass(this);
        if (_indexFormat == null || firstIndex > _indexCount || indexCount > _indexCount - firstIndex)
        {
            throw new ArgumentException("Indexed draw exceeds the selected index range or has no index buffer.");
        }

        _ = checked(firstInstance + instanceCount);
        PrepareDraw(true);
        NativeMethods.Draw(owner.Native, indexCount, instanceCount, firstIndex, baseVertex, firstInstance, 1);
    }

    public void DrawIndirect(BufferSlice<DrawIndirectArguments> arguments)
    {
        ThrowIfSettingArguments();
        RequireTraditionalProgram();
        owner.ValidatePass(this);
        Direct3D12Buffer<DrawIndirectArguments> buffer = IndirectBuffer(arguments);
        PrepareDraw(false);
        owner.Indirect(buffer.Native, buffer.GpuAddress, arguments.OffsetInBytes, 0);
    }

    public void DrawIndexedIndirect(BufferSlice<DrawIndexedIndirectArguments> arguments)
    {
        ThrowIfSettingArguments();
        RequireTraditionalProgram();
        owner.ValidatePass(this);
        Direct3D12Buffer<DrawIndexedIndirectArguments> buffer = IndirectBuffer(arguments);
        PrepareDraw(true);
        owner.Indirect(buffer.Native, buffer.GpuAddress, arguments.OffsetInBytes, 1);
    }

    public void End()
    {
        ThrowIfSettingArguments();
        owner.EndRender(this);
    }

    private static void ValidateProgramAlive(object pipeline)
    {
        if (pipeline is Direct3D12MeshPipeline mesh)
        {
            mesh.ValidateAlive();
        }
        else
        {
            ((Direct3D12GraphicsPipeline)pipeline).ValidateAlive();
        }
    }

    private void SetIndices<T>(BufferSlice<T> indices, IndexFormat format)
        where T : unmanaged
    {
        ThrowIfSettingArguments();
        owner.ValidatePass(this);
        Direct3D12Buffer<T> buffer = owner.Buffer(indices, BufferUsage.Index);
        NativeMethods.Indices(owner.Native, buffer.GpuAddress, indices.OffsetInBytes, checked((uint)indices.SizeInBytes), format == IndexFormat.Uint16 ? 57u : 42u);
        _indexFormat = format;
        _indexCount = indices.Count;
    }

    private Direct3D12Buffer<T> IndirectBuffer<T>(BufferSlice<T> arguments)
        where T : unmanaged
    {
        if (arguments.Count != 1 || arguments.OffsetInBytes % 4 != 0)
        {
            throw new ArgumentException("Indirect execution requires one four-byte-aligned command record.");
        }

        return owner.Buffer(arguments, BufferUsage.Indirect);
    }

    private void ThrowIfSettingArguments()
    {
        if (_settingArguments)
        {
            throw new InvalidOperationException("Encoder mutation is not allowed while setting arguments.");
        }
    }

    private void ValidateRootSelection()
    {
        ThrowIfSettingArguments();
        owner.ValidatePass(this);
        if (SelectedProgram == null)
        {
            throw new InvalidOperationException("Select a program before setting its arguments.");
        }

        ValidateProgramAlive(SelectedProgram!);
        owner.Owner.RequireReady();
    }

    private void SelectRoot(ShaderRootArguments root, ReadOnlySpan<byte> payload, object pipeline, Direct3D12ArgumentTable? tableHint)
    {
        // User codecs and admission hooks can dispose the command owner or pipeline.
        // Revalidate the captured selection before issuing commands or publishing metadata.
        owner.ValidatePass(this);
        if (pipeline is Direct3D12MeshPipeline mesh)
        {
            mesh.ValidateAlive();
        }
        else
        {
            ((Direct3D12GraphicsPipeline)pipeline).ValidateAlive();
        }

        if (!ReferenceEquals(SelectedProgram, pipeline))
        {
            throw new InvalidOperationException("The selected program changed while setting arguments.");
        }

        if (_table != null && tableHint != null && !ReferenceEquals(_table, tableHint))
        {
            throw new ArgumentException("Shader resources belong to a different explicit argument table.");
        }

        tableHint?.ThrowIfDisposed();
        _table?.ThrowIfDisposed();

        // All managed preparation precedes the first native mutation.
        // Issued driver commands cannot be rolled back after recording.
        fixed (byte* bytes = payload)
        {
            owner.Owner.Check(NativeMethods.RootConstants(owner.Native, (uint*)bytes, checked((uint)(payload.Length / 4)), 0));
        }

        _arguments = root;
        _argumentTableHint = tableHint;
        _argumentProgram = pipeline;
    }

    private void RequireTraditionalProgram()
    {
        if (_meshPipeline != null)
        {
            throw new InvalidOperationException("Traditional draws cannot use a mesh pipeline.");
        }
    }

    private void PrepareDraw(bool indexed, bool mesh = false)
    {
        owner.ValidatePass(this);
        if ((mesh ? _meshPipeline == null : _pipeline == null) || _state == null || !_viewport || !_scissor || !_blend || !_stencil)
        {
            throw new InvalidOperationException("Set a program, draw state and all dynamic state before drawing.");
        }

        if (RequiresArguments && _arguments != null && !ReferenceEquals(_argumentProgram, SelectedProgram))
        {
            throw new InvalidOperationException("Set arguments again after switching to a different program.");
        }

        if (RequiresArguments && _arguments == null)
        {
            throw new InvalidOperationException("Set shader arguments before drawing or dispatching.");
        }

        Direct3D12ArgumentTable? table = _table ?? _argumentTableHint;
        if (_table != null && _argumentTableHint != null && !ReferenceEquals(_table, _argumentTableHint))
        {
            throw new ArgumentException("Shader resources belong to a different explicit argument table.");
        }

        table?.ThrowIfDisposed();
        IShaderReference[]? registrations = table?.SnapshotRegistrations();

        if (indexed)
        {
            if (_indexFormat == null)
            {
                throw new InvalidOperationException("Set an index buffer before indexed execution.");
            }
        }

        ValidateProgramAlive(SelectedProgram!);

        nint pipeline = mesh ? _meshPipeline!.Resolve(_state, _formats, _depthFormat) : _pipeline!.Resolve(_state, _formats, _depthFormat, indexed ? _indexFormat : null);
        owner.Owner.Check(NativeMethods.Pipeline(owner.Native, pipeline, 0));
        if (!mesh)
        {
            NativeMethods.Topology(owner.Native, (uint)_state.Desc.Topology + 1);
        }

        if (registrations != null)
        {
            var binding = new Direct3D12ShaderBinding(owner.Owner, table!, registrations);
            owner.KeepBinding(binding);
            NativeMethods.Binding(owner.Native, binding.Native, 0);
        }
    }
}
