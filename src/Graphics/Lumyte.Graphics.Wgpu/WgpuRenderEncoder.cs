using Ahjo.Wgpu.Native;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Wgpu;

internal sealed unsafe class WgpuRenderEncoder(WgpuCommandBuffer owner, WGPURenderPassEncoderImpl* handle, RenderColorAttachmentDesc[] attachments, RenderDepthStencilAttachmentDesc? depth) : IRenderEncoder
{
    private readonly TextureFormat[] _formats = attachments.Select(a => a.View.Info.Format).ToArray();
    private readonly (uint Width, uint Height) _size = (attachments.FirstOrDefault()?.View ?? depth!.View).Texture.GetMipSize((attachments.FirstOrDefault()?.View ?? depth!.View).Info.BaseMipLevel);
    private readonly TextureFormat? _depthFormat = depth?.View.Info.Format;
    private WgpuGraphicsPipeline? _pipeline;
    private RenderStateSnapshot? _state;
    private IndexFormat? _indexFormat;
    private ulong _indexCount;
    private bool _viewport;
    private bool _scissor;
    private bool _blend;
    private bool _stencil;

    private ShaderValueSnapshot? _arguments;
    private object? _argumentProgram;

    public void SetArgumentTable(IArgumentTable table)
    {
        owner.ValidatePass(this);
        ArgumentNullException.ThrowIfNull(table);
        if (table is not WgpuArgumentTable concrete || !concrete.BelongsTo(owner.Owner))
        {
            throw new ArgumentException("Argument table belongs to another device.", nameof(table));
        }

        concrete.ThrowIfDisposed();
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
        _arguments = snapshot;
        _argumentProgram = _pipeline;
    }

    public void SetPipeline(IGraphicsPipeline pipeline)
    {
        owner.ValidatePass(this);
        ArgumentNullException.ThrowIfNull(pipeline);
        if (pipeline is not WgpuGraphicsPipeline program || !ReferenceEquals(program.Owner, owner.Owner))
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

        WGPU.wgpuRenderPassEncoderSetViewport(handle, viewport.X, viewport.Y, viewport.Width, viewport.Height, viewport.MinDepth, viewport.MaxDepth);
        _viewport = true;
    }

    public void SetScissor(ScissorRect scissor)
    {
        owner.ValidatePass(this);
        PipelineValidation.Scissor(scissor, _size.Width, _size.Height);
        WGPU.wgpuRenderPassEncoderSetScissorRect(handle, scissor.X, scissor.Y, scissor.Width, scissor.Height);
        _scissor = true;
    }

    public void SetBlendConstant(BlendConstant value)
    {
        owner.ValidatePass(this);
        if (!float.IsFinite(value.Red) || !float.IsFinite(value.Green) || !float.IsFinite(value.Blue) || !float.IsFinite(value.Alpha))
        {
            throw new ArgumentException("Blend constants must be finite.");
        }

        var native = new WGPUColor { r = value.Red, g = value.Green, b = value.Blue, a = value.Alpha };
        WGPU.wgpuRenderPassEncoderSetBlendConstant(handle, &native);
        _blend = true;
    }

    public void SetStencilReference(uint reference)
    {
        owner.ValidatePass(this);
        if (reference > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(reference));
        }

        WGPU.wgpuRenderPassEncoderSetStencilReference(handle, reference);
        _stencil = true;
    }

    public void SetIndexBuffer(BufferSlice<ushort> indices) => SetIndices(indices, IndexFormat.Uint16);

    public void SetIndexBuffer(BufferSlice<uint> indices) => SetIndices(indices, IndexFormat.Uint32);

    public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
    {
        _ = checked(firstVertex + vertexCount);
        _ = checked(firstInstance + instanceCount);
        PrepareDraw(false);
        WGPU.wgpuRenderPassEncoderDraw(handle, vertexCount, instanceCount, firstVertex, firstInstance);
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
        WGPU.wgpuRenderPassEncoderDrawIndexed(handle, indexCount, instanceCount, firstIndex, baseVertex, firstInstance);
    }

    public void DrawIndirect(BufferSlice<DrawIndirectArguments> arguments)
    {
        owner.ValidatePass(this);
        WgpuBuffer<DrawIndirectArguments> buffer = IndirectBuffer(arguments);
        PrepareDraw(false);
        WGPU.wgpuRenderPassEncoderDrawIndirect(handle, buffer.Native.Handle, arguments.OffsetInBytes);
    }

    public void DrawIndexedIndirect(BufferSlice<DrawIndexedIndirectArguments> arguments)
    {
        owner.ValidatePass(this);
        WgpuBuffer<DrawIndexedIndirectArguments> buffer = IndirectBuffer(arguments);
        PrepareDraw(true);
        WGPU.wgpuRenderPassEncoderDrawIndexedIndirect(handle, buffer.Native.Handle, arguments.OffsetInBytes);
    }

    public void End() => owner.EndRender(this, handle);

    private void SetIndices<T>(BufferSlice<T> indices, IndexFormat format)
        where T : unmanaged
    {
        owner.ValidatePass(this);
        WgpuBuffer<T> buffer = owner.Buffer(indices, BufferUsage.Index);
        WGPU.wgpuRenderPassEncoderSetIndexBuffer(handle, buffer.Native.Handle, format == IndexFormat.Uint16 ? WGPUIndexFormat.Uint16 : WGPUIndexFormat.Uint32, indices.OffsetInBytes, indices.SizeInBytes);
        _indexFormat = format;
        _indexCount = indices.Count;
    }

    private WgpuBuffer<T> IndirectBuffer<T>(BufferSlice<T> arguments)
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

        ShaderBindingData? bindingData = null;
        if (_arguments != null)
        {
            var snapshot = ShaderBindingSnapshot.Capture(_arguments, owner.ReadShaderData);
            bindingData = new(snapshot, ShaderDataLayout.RootTarget(_pipeline.VertexData, _pipeline.FragmentData, snapshot.Root.RootParameter), owner.Owner.Caps);
        }

        if (indexed)
        {
            if (_indexFormat == null)
            {
                throw new InvalidOperationException("Set an index buffer before indexed execution.");
            }
        }

        _pipeline.ValidateAlive();

        WGPURenderPipelineImpl* pipeline = _pipeline.Resolve(_state, _formats, bindingData, _depthFormat, indexed ? _indexFormat : null);
        WGPU.wgpuRenderPassEncoderSetPipeline(handle, pipeline);
        if (bindingData != null)
        {
            UseBinding(bindingData, pipeline);
        }
    }

    private void UseBinding(ShaderBindingData data, WGPURenderPipelineImpl* pipeline)
    {
        WGPUBindGroupLayoutImpl* layout = WGPU.wgpuRenderPipelineGetBindGroupLayout(pipeline, 0);
        try
        {
            var binding = new WgpuShaderBinding(owner.Owner, data, layout);
            owner.KeepBinding(binding);
            WGPU.wgpuRenderPassEncoderSetBindGroup(handle, 0, binding.Native, 0, null);
        }
        finally
        {
            WGPU.wgpuBindGroupLayoutRelease(layout);
        }
    }
}
