using System.Runtime.InteropServices.JavaScript;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal sealed class BrowserRenderEncoder(BrowserCommandBuffer owner, JSObject handle, RenderColorAttachmentDesc[] attachments, RenderDepthStencilAttachmentDesc? depth) : IRenderEncoder
{
    private readonly TextureFormat[] _formats = attachments.Select(a => a.View.Info.Format).ToArray();
    private readonly (uint Width, uint Height) _size = (attachments.FirstOrDefault()?.View ?? depth!.View).Texture.GetMipSize((attachments.FirstOrDefault()?.View ?? depth!.View).Info.BaseMipLevel);
    private readonly TextureFormat? _depthFormat = depth?.View.Info.Format;
    private BrowserGraphicsPipeline? _pipeline;
    private RenderStateSnapshot? _state;
    private IndexFormat? _indexFormat;
    private ulong _indexCount;
    private bool _viewport;
    private bool _scissor;
    private bool _blend;
    private bool _stencil;

    private BrowserRootStorage? _arguments;
    private object? _argumentProgram;
    private Type? _argumentsType;
    private bool _settingArguments;
    private BrowserCommandBuffer.PreparedEntry? _preparedArguments;
    private BrowserArgumentTable? _table;
    private BrowserArgumentTable? _currentRootHint;

    public void SetArgumentTable(IArgumentTable table)
    {
        owner.ValidatePass(this);
        ArgumentNullException.ThrowIfNull(table);
        if (table is not BrowserArgumentTable selected || !selected.BelongsTo(owner.Owner))
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
            throw new NotSupportedException("Use ValidationDevice for argument validation.");
        }
    }

    public void SetArguments<T>(in T value)
        where T : struct, IShaderArguments
    {
        ThrowIfSettingArguments();
        ValidateRootSelection();
        BrowserGraphicsPipeline program = _pipeline!;
        if (value.NativePreparationIdentity is { } identity)
        {
            BrowserCommandBuffer.PreparedEntry prepared = owner.FindPrepared(identity, typeof(T), program, _table, _state?.Key, _formats, _depthFormat, _indexFormat);
            _argumentsType = typeof(T);
            _preparedArguments = prepared;
            _arguments = prepared.Root;
            _argumentProgram = program;
            _currentRootHint = prepared.Root.ImplicitTable;
            return;
        }

        _settingArguments = true;
        try
        {
            BrowserRootStorage root = BrowserShaderTransfer.CreateRoot(owner.Owner, in value);
            SelectRoot(root, program);
        }
        finally
        {
            _settingArguments = false;
        }
    }

    public void SetArguments<T>(in T value, string rootParameter = "arguments", string? shaderTypeName = null)
        where T : unmanaged
    {
        ThrowIfSettingArguments();
        ValidateRootSelection();
        BrowserGraphicsPipeline program = _pipeline!;
        _settingArguments = true;
        try
        {
            BrowserRootStorage root = BrowserShaderTransfer.CreateUnmanaged(owner.Owner, in value, rootParameter, shaderTypeName);
            SelectRoot(root, program);
        }
        finally
        {
            _settingArguments = false;
        }
    }

    public void SetPipeline(IMeshPipeline pipeline)
    {
        ThrowIfSettingArguments();
        owner.ValidatePass(this);
        ArgumentNullException.ThrowIfNull(pipeline);
        throw new NotSupportedException("Browser does not support Mesh/Task pipelines.");
    }

    public void DrawMeshTasks(uint x, uint y = 1, uint z = 1)
    {
        ThrowIfSettingArguments();
        owner.ValidatePass(this);
        throw new InvalidOperationException("No Mesh pipeline can be selected on this backend.");
    }

    public void SetPipeline(IGraphicsPipeline pipeline)
    {
        ThrowIfSettingArguments();
        owner.ValidatePass(this);
        ArgumentNullException.ThrowIfNull(pipeline);
        if (pipeline is not BrowserGraphicsPipeline program || !ReferenceEquals(program.Owner, owner.Owner))
        {
            throw new ArgumentException("Pipeline belongs to another device.");
        }

        program.ValidateAlive();
        _pipeline = program;
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

        BrowserInterop.SetViewport(handle, viewport.X, viewport.Y, viewport.Width, viewport.Height, viewport.MinDepth, viewport.MaxDepth);
        _viewport = true;
    }

    public void SetScissor(ScissorRect scissor)
    {
        ThrowIfSettingArguments();
        owner.ValidatePass(this);
        PipelineValidation.Scissor(scissor, _size.Width, _size.Height);
        BrowserInterop.SetScissor(handle, scissor.X, scissor.Y, scissor.Width, scissor.Height);
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

        BrowserInterop.SetBlendConstant(handle, value.Red, value.Green, value.Blue, value.Alpha);
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

        BrowserInterop.SetStencilReference(handle, (int)reference);
        _stencil = true;
    }

    public void SetIndexBuffer(BufferSlice<ushort> indices) => SetIndices(indices, IndexFormat.Uint16);

    public void SetIndexBuffer(BufferSlice<uint> indices) => SetIndices(indices, IndexFormat.Uint32);

    public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
    {
        ThrowIfSettingArguments();
        _ = checked(firstVertex + vertexCount);
        _ = checked(firstInstance + instanceCount);
        PrepareDraw(false);
        BrowserInterop.Draw(handle, vertexCount, instanceCount, firstVertex, firstInstance);
    }

    public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0)
    {
        ThrowIfSettingArguments();
        owner.ValidatePass(this);
        if (_indexFormat == null || firstIndex > _indexCount || indexCount > _indexCount - firstIndex)
        {
            throw new ArgumentException("Indexed draw exceeds the selected index range or has no index buffer.");
        }

        _ = checked(firstInstance + instanceCount);
        PrepareDraw(true);
        BrowserInterop.DrawIndexed(handle, indexCount, instanceCount, firstIndex, baseVertex, firstInstance);
    }

    public void DrawIndirect(BufferSlice<DrawIndirectArguments> arguments)
    {
        ThrowIfSettingArguments();
        owner.ValidatePass(this);
        BrowserBuffer<DrawIndirectArguments> buffer = IndirectBuffer(arguments);
        PrepareDraw(false);
        BrowserInterop.DrawIndirect(handle, buffer.Native, arguments.OffsetInBytes, false);
    }

    public void DrawIndexedIndirect(BufferSlice<DrawIndexedIndirectArguments> arguments)
    {
        ThrowIfSettingArguments();
        owner.ValidatePass(this);
        BrowserBuffer<DrawIndexedIndirectArguments> buffer = IndirectBuffer(arguments);
        PrepareDraw(true);
        BrowserInterop.DrawIndirect(handle, buffer.Native, arguments.OffsetInBytes, true);
    }

    public void End()
    {
        ThrowIfSettingArguments();
        owner.EndRender(this, handle);
    }

    private void SetIndices<T>(BufferSlice<T> indices, IndexFormat format)
        where T : unmanaged
    {
        owner.ValidatePass(this);
        if (_preparedArguments is { } prepared && !prepared.Matches(_argumentsType!, _pipeline!, _table, _state?.Key, _formats, _depthFormat, format))
        {
            throw new InvalidOperationException("Selected index format differs from native preparation.");
        }

        BrowserBuffer<T> buffer = owner.Buffer(indices, BufferUsage.Index);
        BrowserInterop.SetIndexBuffer(handle, buffer.Native, (int)format, indices.OffsetInBytes, indices.SizeInBytes);
        _indexFormat = format;
        _indexCount = indices.Count;
    }

    private BrowserBuffer<T> IndirectBuffer<T>(BufferSlice<T> arguments)
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
            throw new InvalidOperationException("Root argument preparation is already active.");
        }
    }

    private void ValidateRootSelection()
    {
        ThrowIfSettingArguments();
        owner.ValidatePass(this);
        if (_pipeline == null)
        {
            throw new InvalidOperationException("Select a program before setting its arguments.");
        }

        _pipeline.ValidateAlive();
    }

    private void SelectRoot(BrowserRootStorage root, BrowserGraphicsPipeline program)
    {
        try
        {
            owner.ValidatePass(this);
            program.ValidateAlive();
            if (!ReferenceEquals(_pipeline, program))
            {
                throw new InvalidOperationException("The selected program changed while preparing root arguments.");
            }

            if (_table != null && root.ImplicitTable != null && !ReferenceEquals(_table, root.ImplicitTable))
            {
                throw new ArgumentException("Selected table differs from root references.");
            }

            owner.KeepBinding(root);
        }
        catch
        {
            try
            {
                root.Dispose();
            }
            catch
            {
                // Preserve the original transfer or selection failure.
            }

            throw;
        }

        _preparedArguments = null;
        _currentRootHint = root.ImplicitTable;
        _arguments = root;
        _argumentProgram = program;
    }

    private void PrepareDraw(bool indexed)
    {
        owner.ValidatePass(this);
        if (indexed && _indexFormat == null)
        {
            throw new InvalidOperationException("Set an index buffer before indexed execution.");
        }

        if (_pipeline == null || _state == null || !_viewport || !_scissor || !_blend || !_stencil)
        {
            throw new InvalidOperationException("Set a program, draw state and all dynamic state before drawing.");
        }

        if (_pipeline.RequiresArguments && _arguments != null && !ReferenceEquals(_argumentProgram, _pipeline))
        {
            throw new InvalidOperationException("Set arguments again after switching to a different program.");
        }

        if (_pipeline.RequiresArguments && _arguments == null)
        {
            throw new InvalidOperationException("Set arguments before drawing or dispatching.");
        }

        if (_table != null && _currentRootHint != null && !ReferenceEquals(_table, _currentRootHint))
        {
            throw new ArgumentException("Selected table differs from root references.");
        }

        if (_preparedArguments is { } prepared)
        {
            _pipeline.ValidateAlive();
            if (!prepared.Matches(_argumentsType!, _pipeline, _table, _state.Key, _formats, _depthFormat, indexed ? _indexFormat : null))
            {
                throw new InvalidOperationException("Selected execution state differs from native preparation.");
            }

            prepared.Validate();
            BrowserInterop.SetRenderPipeline(handle, prepared.Render!);
            if (prepared.Binding != null)
            {
                BrowserInterop.SetShaderBinding(handle, prepared.Binding.Native);
            }

            return;
        }

        BrowserArgumentTable? selectedTable = _table ?? _currentRootHint;
        selectedTable?.ThrowIfDisposed();
        IReadOnlyCollection<IShaderReference> registrations = selectedTable?.SnapshotRegistrations() ?? Array.Empty<IShaderReference>();
        ShaderBindingData? bindingData = null;
        if (_pipeline.RequiresArguments || selectedTable != null)
        {
            bindingData = new(registrations, _pipeline.RequiresArguments ? ShaderDataLayout.RootTarget(_pipeline.VertexData, _pipeline.FragmentData, _arguments!.Arguments.RootParameter) : _pipeline.VertexData, owner.Owner.Caps);
        }

        _pipeline.ValidateAlive();

        JSObject pipeline = _pipeline.Resolve(_state, _formats, bindingData, _depthFormat, indexed ? _indexFormat : null);
        BrowserInterop.SetRenderPipeline(handle, pipeline);
        if (bindingData != null)
        {
            var binding = new BrowserShaderBinding(owner.Owner, bindingData, _pipeline.RequiresArguments ? _arguments : null, selectedTable, registrations);
            owner.KeepBinding(binding);
            BrowserInterop.SetShaderBinding(handle, binding.Native);
        }
    }
}
