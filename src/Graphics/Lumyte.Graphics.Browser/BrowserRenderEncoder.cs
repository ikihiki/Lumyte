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
    private Action? _validateIndices;
    private bool _viewport;
    private bool _scissor;
    private bool _blend;
    private bool _stencil;

    private BrowserArgumentTable? _argumentTable;
    private ShaderValueSnapshot? _arguments;
    private object? _argumentProgram;

    public void SetArgumentTable(IArgumentTable table)
    {
        owner.ValidatePass(this);
        ArgumentNullException.ThrowIfNull(table);
        if (table is not BrowserArgumentTable concrete || !concrete.BelongsTo(owner.Owner))
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
                if (member.Reference is not IShaderReference reference || reference.Table is not BrowserArgumentTable referenceTable || !referenceTable.BelongsTo(owner.Owner))
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
        if (pipeline is not BrowserGraphicsPipeline program || !ReferenceEquals(program.Owner, owner.Owner))
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

        BrowserInterop.SetViewport(handle, viewport.X, viewport.Y, viewport.Width, viewport.Height, viewport.MinDepth, viewport.MaxDepth);
        _viewport = true;
    }

    public void SetScissor(ScissorRect scissor)
    {
        owner.ValidatePass(this);
        PipelineValidation.Scissor(scissor, _size.Width, _size.Height);
        BrowserInterop.SetScissor(handle, scissor.X, scissor.Y, scissor.Width, scissor.Height);
        _scissor = true;
    }

    public void SetBlendConstant(BlendConstant value)
    {
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
        _ = checked(firstVertex + vertexCount);
        _ = checked(firstInstance + instanceCount);
        PrepareDraw(false);
        BrowserInterop.Draw(handle, vertexCount, instanceCount, firstVertex, firstInstance);
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
        BrowserInterop.DrawIndexed(handle, indexCount, instanceCount, firstIndex, baseVertex, firstInstance);
    }

    public void DrawIndirect(BufferSlice<DrawIndirectArguments> arguments)
    {
        owner.ValidatePass(this);
        BrowserBuffer<DrawIndirectArguments> buffer = IndirectBuffer(arguments);
        PrepareDraw(false);
        BrowserInterop.DrawIndirect(handle, buffer.Native, arguments.OffsetInBytes, false);
        owner.TrackProgram(() => { _ = buffer.Native; });
    }

    public void DrawIndexedIndirect(BufferSlice<DrawIndexedIndirectArguments> arguments)
    {
        owner.ValidatePass(this);
        BrowserBuffer<DrawIndexedIndirectArguments> buffer = IndirectBuffer(arguments);
        PrepareDraw(true);
        BrowserInterop.DrawIndirect(handle, buffer.Native, arguments.OffsetInBytes, true);
        owner.TrackProgram(() => { _ = buffer.Native; });
    }

    public void End() => owner.EndRender(this, handle);

    private void SetIndices<T>(BufferSlice<T> indices, IndexFormat format)
        where T : unmanaged
    {
        owner.ValidatePass(this);
        BrowserBuffer<T> buffer = owner.Buffer(indices, BufferUsage.Index);
        BrowserInterop.SetIndexBuffer(handle, buffer.Native, (int)format, indices.OffsetInBytes, indices.SizeInBytes);
        _indexFormat = format;
        _indexCount = indices.Count;
        _validateIndices = () => { _ = buffer.Native; };
        owner.TrackProgram(_validateIndices);
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

        ShaderBindingData? bindingData = null;
        if (_arguments != null)
        {
            if (_argumentTable == null && _arguments.Values.Any(v => v.IsReference))
            {
                throw new InvalidOperationException("Select an argument table before using shader arguments.");
            }

            _argumentTable?.ThrowIfDisposed();
            var snapshot = ShaderBindingSnapshot.Capture((object?)_argumentTable ?? this, _arguments, owner.ReadShaderData);
            bindingData = new(snapshot, ShaderDataLayout.RootTarget(_pipeline.VertexData, _pipeline.FragmentData, snapshot.Root.RootParameter), owner.Owner.Caps);
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

        JSObject pipeline = _pipeline.Resolve(_state, _formats, bindingData, _depthFormat, indexed ? _indexFormat : null);
        BrowserInterop.SetRenderPipeline(handle, pipeline);
        if (bindingData != null)
        {
            var binding = new BrowserShaderBinding(owner.Owner, bindingData);
            owner.KeepBinding(binding);
            BrowserInterop.SetShaderBinding(handle, binding.Native);
        }

        if (bindingData != null)
        {
            owner.TrackShaderSnapshot(bindingData.Snapshot);
        }

        owner.TrackProgram(_pipeline.ValidateAlive);
    }
}
