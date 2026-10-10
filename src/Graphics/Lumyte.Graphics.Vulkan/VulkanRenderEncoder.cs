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
    private Action? _validateIndices;
    private bool _viewport;
    private bool _scissor;
    private bool _blend;
    private bool _stencil;

    private VulkanArgumentTable? _argumentTable;
    private ShaderValueSnapshot? _arguments;
    private object? _argumentProgram;

    public void SetArgumentTable(IArgumentTable table)
    {
        owner.ValidatePass(this);
        ArgumentNullException.ThrowIfNull(table);
        if (table is not VulkanArgumentTable concrete || !concrete.BelongsTo(owner.Owner))
        {
            throw new ArgumentException("Argument table belongs to another device.", nameof(table));
        }

        concrete.ThrowIfDisposed();
        _argumentTable = concrete;
    }

    public void SetArguments<T>(in T value)
        where T : struct, IShaderArguments
    {
        owner.ValidatePass(this);
        if (_pipeline == null)
        {
            throw new InvalidOperationException("Select a program before setting its arguments.");
        }

        _pipeline.ValidateAlive();
        ShaderValueSnapshot snapshot = IShaderArguments.Capture(in value);
        ShaderDataLayout.Root(ShaderDataLayout.RootTarget(_pipeline.VertexData, _pipeline.FragmentData, snapshot.RootParameter), snapshot.RootParameter).Validate(snapshot);
        foreach (ShaderValue member in snapshot.Values)
        {
            if (member.IsReference)
            {
                if (member.Reference is not IShaderReference reference || reference.Table is not VulkanArgumentTable referenceTable || !referenceTable.BelongsTo(owner.Owner))
                {
                    throw new ArgumentException("Root arguments contain a missing or foreign backend reference.", nameof(value));
                }

                reference.Validate();
            }
        }

        _arguments = snapshot;
        _argumentProgram = _pipeline;
    }

    public void SetPipeline(IGraphicsPipeline pipeline)
    {
        owner.ValidatePass(this);
        ArgumentNullException.ThrowIfNull(pipeline);
        if (pipeline is not VulkanGraphicsPipeline program || !ReferenceEquals(program.Owner, owner.Owner))
        {
            throw new ArgumentException("Pipeline belongs to another device.");
        }

        program.ValidateAlive();
        _pipeline = program;
    }

    public void SetRenderState(GraphicsRenderStateDesc state)
    {
        owner.ValidatePass(this);
        RenderStateSnapshot snapshot = PipelineValidation.State(state);
        _state = snapshot;
    }

    public void SetViewport(Viewport viewport)
    {
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
        owner.ValidatePass(this);
        PipelineValidation.Scissor(scissor, _size.Width, _size.Height);
        var native = new V.Rect2D(new(checked((int)scissor.X), checked((int)scissor.Y)), new(scissor.Width, scissor.Height));
        owner.Owner.Api.CmdSetScissor(owner.Native, 0, 1, &native);
        _scissor = true;
    }

    public void SetBlendConstant(BlendConstant value)
    {
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
        _ = checked(firstVertex + vertexCount);
        _ = checked(firstInstance + instanceCount);
        PrepareDraw(false);
        owner.Owner.Api.CmdDraw(owner.Native, vertexCount, instanceCount, firstVertex, firstInstance);
    }

    public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0)
    {
        owner.ValidatePass(this);
        if (_indexFormat == null || firstIndex > _indexCount || indexCount > _indexCount - firstIndex)
        {
            throw new ArgumentException("Indexed draw exceeds the selected index range or has no index buffer.");
        }

        _ = checked(firstInstance + instanceCount);
        PrepareDraw(true);
        owner.Owner.Api.CmdDrawIndexed(owner.Native, indexCount, instanceCount, firstIndex, baseVertex, firstInstance);
    }

    public void DrawIndirect(BufferSlice<DrawIndirectArguments> arguments)
    {
        owner.ValidatePass(this);
        VulkanBuffer<DrawIndirectArguments> buffer = IndirectBuffer(arguments);
        PrepareDraw(false);
        owner.Owner.Api.CmdDrawIndirect(owner.Native, buffer.Native, arguments.OffsetInBytes, 1, 16);
        owner.TrackProgram(() => { _ = buffer.Native; });
    }

    public void DrawIndexedIndirect(BufferSlice<DrawIndexedIndirectArguments> arguments)
    {
        owner.ValidatePass(this);
        VulkanBuffer<DrawIndexedIndirectArguments> buffer = IndirectBuffer(arguments);
        PrepareDraw(true);
        owner.Owner.Api.CmdDrawIndexedIndirect(owner.Native, buffer.Native, arguments.OffsetInBytes, 1, 20);
        owner.TrackProgram(() => { _ = buffer.Native; });
    }

    public void End() => owner.EndRender(this);

    private void SetIndices<T>(BufferSlice<T> indices, IndexFormat format)
        where T : unmanaged
    {
        owner.ValidatePass(this);
        VulkanBuffer<T> buffer = owner.Buffer(indices, BufferUsage.Index);
        owner.Owner.Api.CmdBindIndexBuffer(owner.Native, buffer.Native, indices.OffsetInBytes, format == IndexFormat.Uint16 ? V.IndexType.Uint16 : V.IndexType.Uint32);
        _indexFormat = format;
        _indexCount = indices.Count;
        _validateIndices = () => { _ = buffer.Native; };
        owner.TrackProgram(_validateIndices);
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

    private void PrepareDraw(bool indexed)
    {
        owner.ValidatePass(this);
        if (_pipeline == null || _state == null || !_viewport || !_scissor || !_blend || !_stencil)
        {
            throw new InvalidOperationException("Set a program, draw state and all dynamic state before drawing.");
        }

        if (_arguments != null && !ReferenceEquals(_argumentProgram, _pipeline))
        {
            throw new InvalidOperationException("Set arguments again after switching to a different program.");
        }

        if (_arguments == null && (ShaderDataLayout.HasRoot(_pipeline.VertexData) || (_pipeline.FragmentData != null && ShaderDataLayout.HasRoot(_pipeline.FragmentData))))
        {
            throw new InvalidOperationException("Set the program's root arguments before execution.");
        }

        ShaderBindingSnapshot? bindingSnapshot = null;
        if (_arguments != null)
        {
            if (_argumentTable == null && _arguments.Values.Any(v => v.IsReference))
            {
                throw new InvalidOperationException("Select an argument table before using shader arguments.");
            }

            _argumentTable?.ThrowIfDisposed();
            var snapshot = ShaderBindingSnapshot.Capture((object?)_argumentTable ?? this, _arguments, owner.ReadShaderData);
            bindingSnapshot = snapshot;
        }

        if (indexed)
        {
            if (_indexFormat == null || _validateIndices == null)
            {
                throw new InvalidOperationException("Set an index buffer before indexed execution.");
            }

            _validateIndices();
        }

        _pipeline.ValidateAlive();
        PipelineValidation.Draw(_pipeline.Desc, _state, _formats, _pipeline.FragmentOutputs, _depthFormat, indexed ? _indexFormat : null);

        V.Pipeline pipeline = _pipeline.Resolve(_state, _formats, bindingSnapshot, _depthFormat, indexed ? _indexFormat : null);
        owner.Owner.Api.CmdBindPipeline(owner.Native, V.PipelineBindPoint.Graphics, pipeline);
        if (bindingSnapshot != null)
        {
            var binding = new VulkanShaderBinding(owner.Owner, bindingSnapshot, ShaderDataLayout.RootTarget(_pipeline.VertexData, _pipeline.FragmentData, bindingSnapshot.Root.RootParameter), _pipeline.ArgumentLayout);
            owner.KeepBinding(binding);
            V.DescriptorSet set = binding.Native;
            owner.Owner.Api.CmdBindDescriptorSets(owner.Native, V.PipelineBindPoint.Graphics, _pipeline.ArgumentPipelineLayout, 0, 1, &set, 0, null);
        }

        if (bindingSnapshot != null)
        {
            owner.TrackProgram(bindingSnapshot.Validate);
        }

        owner.TrackProgram(_pipeline.ValidateAlive);
    }
}
