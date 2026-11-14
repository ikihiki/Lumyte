using System.Runtime.CompilerServices;
using Lumyte.Graphics.Abstractions;
using V = Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

internal sealed unsafe class VulkanRenderEncoder(VulkanCommandBuffer owner, RenderColorAttachmentDesc[] attachments, RenderDepthStencilAttachmentDesc? depth) : IRenderEncoder
{
    private readonly TextureFormat[] _formats = attachments.Select(a => a.View.Info.Format).ToArray();
    private readonly (uint Width, uint Height) _size = (attachments.FirstOrDefault()?.View ?? depth!.View).Texture.GetMipSize((attachments.FirstOrDefault()?.View ?? depth!.View).Info.BaseMipLevel);
    private readonly TextureFormat? _depthFormat = depth?.View.Info.Format;
    private VulkanGraphicsPipeline? _pipeline;
    private RenderStateSnapshot? _state;
    private IndexFormat? _indexFormat;
    private ulong _indexCount;
    private object? _indexBuffer;
    private ulong _indexOffset;
    private bool _viewport;
    private bool _scissor;
    private bool _blend;
    private bool _stencil;

    private Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? _argumentValidation;
    private ShaderRootArguments? _arguments;
    private object? _argumentProgram;
    private bool _settingArguments;
    private VulkanArgumentTable? _table;

    public void SetArgumentTable(IArgumentTable table)
    {
        // Graph admission legitimately autobinds its table here; it does not change root bytes.
        owner.ValidatePass(this);
        ArgumentNullException.ThrowIfNull(table);
        if (table is not VulkanArgumentTable selected || !selected.BelongsTo(owner.Owner))
        {
            throw new ArgumentException("Argument table belongs to another device.", nameof(table));
        }

        selected.ThrowIfDisposed();
        owner.Owner.Validation?.Use(owner, selected);
        _table = selected;
    }

    public void SetArgumentValidation(Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare)
    {
        ThrowIfSettingArguments();
        owner.ValidatePass(this);
        _argumentValidation = prepare;
    }

    public void SetArguments<T>(in T value)
        where T : struct, IShaderArguments
    {
        ValidateRootSelection();
        VulkanGraphicsPipeline pipeline = _pipeline!;
        _settingArguments = true;
        try
        {
            int size = VulkanPushConstants.ValidateStackSize(owner.Owner, T.TransferSize);
            Span<byte> payload = stackalloc byte[size];
            payload.Clear();
            ShaderRootArguments root = VulkanShaderTransfer.CreateRoot(owner.Owner, in value, payload, diagnostic: owner.Owner.Validation == null ? null : DiagnosticRoot(T.RootParameter), prepare: _argumentValidation);
            SelectRoot(root, payload, pipeline);
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
        VulkanGraphicsPipeline pipeline = _pipeline!;
        _settingArguments = true;
        try
        {
            int size = VulkanPushConstants.ValidateStackSize(owner.Owner, Unsafe.SizeOf<T>());
            Span<byte> payload = stackalloc byte[size];
            payload.Clear();
            ShaderRootArguments root = VulkanShaderTransfer.CreateUnmanaged(owner.Owner, in value, rootParameter, shaderTypeName, payload, diagnostic: DiagnosticRoot(rootParameter), prepare: _argumentValidation);
            SelectRoot(root, payload, pipeline);
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
        if (pipeline is not VulkanGraphicsPipeline program || !ReferenceEquals(program.Owner, owner.Owner))
        {
            throw new ArgumentException("Pipeline belongs to another device.");
        }

        program.ValidateAlive();
        owner.Owner.Validation?.Use(owner, program);
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

        var native = new V.Viewport(viewport.X, viewport.Y + viewport.Height, viewport.Width, -viewport.Height, viewport.MinDepth, viewport.MaxDepth);
        owner.Owner.Api.CmdSetViewport(owner.Native, 0, 1, &native);
        _viewport = true;
    }

    public void SetScissor(ScissorRect scissor)
    {
        ThrowIfSettingArguments();
        owner.ValidatePass(this);
        PipelineValidation.Scissor(scissor, _size.Width, _size.Height);
        var native = new V.Rect2D(new(checked((int)scissor.X), checked((int)scissor.Y)), new(scissor.Width, scissor.Height));
        owner.Owner.Api.CmdSetScissor(owner.Native, 0, 1, &native);
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

        float* constants = stackalloc float[] { value.Red, value.Green, value.Blue, value.Alpha };
        owner.Owner.Api.CmdSetBlendConstants(owner.Native, constants);
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

        owner.Owner.Api.CmdSetStencilReference(owner.Native, V.StencilFaceFlags.FrontAndBack, reference);
        _stencil = true;
    }

    public void SetIndexBuffer(BufferSlice<ushort> indices) => SetIndices(indices, IndexFormat.Uint16);

    public void SetIndexBuffer(BufferSlice<uint> indices) => SetIndices(indices, IndexFormat.Uint32);

    public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
    {
        ThrowIfSettingArguments();
        using GraphicsValidation.RecordingTransaction? validationRecording = owner.Owner.Validation?.BeginRecording(owner);
        _ = checked(firstVertex + vertexCount);
        _ = checked(firstInstance + instanceCount);
        PrepareDraw(false);
        owner.Owner.Api.CmdDraw(owner.Native, vertexCount, instanceCount, firstVertex, firstInstance);
        validationRecording?.Commit();
    }

    public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0)
    {
        ThrowIfSettingArguments();
        using GraphicsValidation.RecordingTransaction? validationRecording = owner.Owner.Validation?.BeginRecording(owner);
        owner.ValidatePass(this);
        if (_indexFormat == null || firstIndex > _indexCount || indexCount > _indexCount - firstIndex)
        {
            throw new ArgumentException("Indexed draw exceeds the selected index range or has no index buffer.");
        }

        _ = checked(firstInstance + instanceCount);
        PrepareDraw(true);
        owner.Owner.Validation?.Access(owner, _indexBuffer!, new(PipelineStage.IndexInput, ResourceAccess.IndexRead), checked(_indexOffset + ((ulong)firstIndex * (_indexFormat == IndexFormat.Uint16 ? 2ul : 4ul))), checked((ulong)indexCount * (_indexFormat == IndexFormat.Uint16 ? 2ul : 4ul)));
        owner.Owner.Api.CmdDrawIndexed(owner.Native, indexCount, instanceCount, firstIndex, baseVertex, firstInstance);
        validationRecording?.Commit();
    }

    public void DrawIndirect(BufferSlice<DrawIndirectArguments> arguments)
    {
        ThrowIfSettingArguments();
        using GraphicsValidation.RecordingTransaction? validationRecording = owner.Owner.Validation?.BeginRecording(owner);
        owner.ValidatePass(this);
        VulkanBuffer<DrawIndirectArguments> buffer = IndirectBuffer(arguments);
        PrepareDraw(false);
        owner.Owner.Validation?.Access(owner, buffer, new(PipelineStage.DrawIndirect, ResourceAccess.IndirectRead), arguments.OffsetInBytes, arguments.SizeInBytes);
        owner.Owner.Api.CmdDrawIndirect(owner.Native, buffer.Native, arguments.OffsetInBytes, 1, 16);
        validationRecording?.Commit();
    }

    public void DrawIndexedIndirect(BufferSlice<DrawIndexedIndirectArguments> arguments)
    {
        ThrowIfSettingArguments();
        using GraphicsValidation.RecordingTransaction? validationRecording = owner.Owner.Validation?.BeginRecording(owner);
        owner.ValidatePass(this);
        VulkanBuffer<DrawIndexedIndirectArguments> buffer = IndirectBuffer(arguments);
        PrepareDraw(true);
        owner.Owner.Validation?.Access(owner, buffer, new(PipelineStage.DrawIndirect, ResourceAccess.IndirectRead), arguments.OffsetInBytes, arguments.SizeInBytes);
        owner.Owner.Validation?.Access(owner, _indexBuffer!, new(PipelineStage.IndexInput, ResourceAccess.IndexRead), _indexOffset, checked(_indexCount * (_indexFormat == IndexFormat.Uint16 ? 2ul : 4ul)));
        owner.Owner.Api.CmdDrawIndexedIndirect(owner.Native, buffer.Native, arguments.OffsetInBytes, 1, 20);
        validationRecording?.Commit();
    }

    public void End()
    {
        ThrowIfSettingArguments();
        owner.EndRender(this);
    }

    private void SetIndices<T>(BufferSlice<T> indices, IndexFormat format)
        where T : unmanaged
    {
        ThrowIfSettingArguments();
        using GraphicsValidation.RecordingTransaction? validationRecording = owner.Owner.Validation?.BeginRecording(owner);
        owner.ValidatePass(this);
        VulkanBuffer<T> buffer = owner.Buffer(indices, BufferUsage.Index);
        owner.Owner.Api.CmdBindIndexBuffer(owner.Native, buffer.Native, indices.OffsetInBytes, format == IndexFormat.Uint16 ? V.IndexType.Uint16 : V.IndexType.Uint32);
        validationRecording?.Commit();
        _indexFormat = format;
        _indexCount = indices.Count;
        if (owner.Owner.Validation != null)
        {
            _indexBuffer = buffer;
            _indexOffset = indices.OffsetInBytes;
        }
    }

    private VulkanBuffer<T> IndirectBuffer<T>(BufferSlice<T> arguments)
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
        if (_pipeline == null)
        {
            throw new InvalidOperationException("Select a program before setting its arguments.");
        }

        _pipeline.ValidateAlive();
    }

    private ShaderDataLayout? DiagnosticRoot(string rootParameter) => owner.Owner.Validation == null ? null : ShaderDataLayout.Root(ShaderDataLayout.RootTarget(_pipeline!.VertexData, _pipeline.FragmentData, rootParameter), rootParameter);

    private void SelectRoot(ShaderRootArguments root, ReadOnlySpan<byte> payload, VulkanGraphicsPipeline pipeline)
    {
        // User codecs and admission hooks can dispose the command owner or pipeline.
        // Revalidate the captured selection before issuing commands or publishing metadata.
        owner.ValidatePass(this);
        pipeline.ValidateAlive();
        if (!ReferenceEquals(_pipeline, pipeline))
        {
            throw new InvalidOperationException("The selected program changed while setting arguments.");
        }

        VulkanPushConstants.Emit(owner, pipeline.ArgumentPipelineLayout, VulkanPushConstants.GraphicsStages, payload);
        _arguments = root;
        _argumentProgram = pipeline;
    }

    private void PrepareDraw(bool indexed)
    {
        owner.ValidatePass(this);
        if (_pipeline == null || _state == null || !_viewport || !_scissor || !_blend || !_stencil)
        {
            throw new InvalidOperationException("Set a program, draw state and all dynamic state before drawing.");
        }

        if (_pipeline.RequiresArguments && _arguments != null && !ReferenceEquals(_argumentProgram, _pipeline))
        {
            throw new InvalidOperationException("Set arguments again after switching to a different program.");
        }

        ShaderBindingDependencies? bindingDependencies = null;
        if (_pipeline.RequiresArguments && _arguments != null)
        {
            _table?.ThrowIfDisposed();
            var dependencies = ShaderBindingDependencies.Collect(_arguments, _table, owner.ReadShaderData);
            if (owner.Owner.Validation is { } validation)
            {
                EncoderValidation.ShaderAccesses(validation, owner, dependencies.References, EncoderValidation.RenderStages(_pipeline.VertexData, _pipeline.FragmentData, dependencies.RootParameter));
            }

            bindingDependencies = dependencies;
        }

        if (indexed)
        {
            if (_indexFormat == null)
            {
                throw new InvalidOperationException("Set an index buffer before indexed execution.");
            }
        }

        _pipeline.ValidateAlive();

        V.Pipeline pipeline = _pipeline.Resolve(_state, _formats, bindingDependencies, _depthFormat, indexed ? _indexFormat : null);
        owner.Owner.Api.CmdBindPipeline(owner.Native, V.PipelineBindPoint.Graphics, pipeline);
        if (bindingDependencies != null)
        {
            var binding = new VulkanShaderBinding(owner.Owner, bindingDependencies, ShaderDataLayout.RootTarget(_pipeline.VertexData, _pipeline.FragmentData, bindingDependencies.RootParameter), _pipeline.ArgumentLayout);
            owner.KeepBinding(binding);
            V.DescriptorSet set = binding.Native;
            owner.Owner.Api.CmdBindDescriptorSets(owner.Native, V.PipelineBindPoint.Graphics, _pipeline.ArgumentPipelineLayout, 0, 1, &set, 0, null);
        }
    }
}
