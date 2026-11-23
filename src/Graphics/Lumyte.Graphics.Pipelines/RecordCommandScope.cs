using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Pipelines;

internal sealed class RecordCommandScope(IGraphicsCommandBuffer commands) : IGraphicsCommandBuffer
{
    private object? _encoder;
    private Action? _end;
    private bool _closed;

    public CommandBufferState State => commands.State;

    public void Finish() => throw new InvalidOperationException("Stages cannot finalize borrowed commands.");

    public void Dispose() => throw new InvalidOperationException("Stages cannot dispose borrowed commands.");

    public void CopyBuffer<TSource, TDestination>(BufferSlice<TSource> source, BufferSlice<TDestination> destination)
        where TSource : unmanaged
        where TDestination : unmanaged
    {
        CheckOutside();
        commands.CopyBuffer<TSource, TDestination>(source, destination);
    }

    public void CopyTexture(TextureCopyRegion source, TextureCopyRegion destination)
    {
        CheckOutside();
        commands.CopyTexture(source, destination);
    }

    public void CopyBufferToTexture(BufferTextureCopyLayout source, TextureCopyRegion destination)
    {
        CheckOutside();
        commands.CopyBufferToTexture(source, destination);
    }

    public void CopyTextureToBuffer(TextureCopyRegion source, BufferTextureCopyLayout destination)
    {
        CheckOutside();
        commands.CopyTextureToBuffer(source, destination);
    }

    public void Barrier(MemoryBarrierDesc barrier)
    {
        CheckOutside();
        commands.Barrier(barrier);
    }

    public void Barrier<TElement>(BufferBarrierDesc<TElement> barrier)
        where TElement : unmanaged
    {
        CheckOutside();
        commands.Barrier<TElement>(barrier);
    }

    public void Barrier(TextureBarrierDesc barrier)
    {
        CheckOutside();
        commands.Barrier(barrier);
    }

    public void CopyBuffer<TArguments>(ShaderDataSlice<TArguments> source, ShaderDataSlice<TArguments> destination)
        where TArguments : struct, IShaderArguments
    {
        CheckOutside();
        commands.CopyBuffer<TArguments>(source, destination);
    }

    public void Barrier<TArguments>(ShaderDataBufferBarrierDesc<TArguments> barrier)
        where TArguments : struct, IShaderArguments
    {
        CheckOutside();
        commands.Barrier<TArguments>(barrier);
    }

    public IRenderEncoder BeginRenderPass(RenderPassDesc desc)
    {
        CheckOutside();
        var encoder = new Render(this, commands.BeginRenderPass(desc));
        _encoder = encoder;
        _end = encoder.End;
        return encoder;
    }

    public IComputeEncoder BeginComputePass(ComputePassDesc desc)
    {
        CheckOutside();
        var encoder = new Compute(this, commands.BeginComputePass(desc));
        _encoder = encoder;
        _end = encoder.End;
        return encoder;
    }

    internal void CloseOwnedEncoder()
    {
        _end?.Invoke();
        _closed = true;
    }

    private void CheckOutside()
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        if (_encoder is not null || commands.State != CommandBufferState.Recording)
        {
            throw new InvalidOperationException("Operation requires an idle record-only scope.");
        }
    }

    private void Verify(object encoder)
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        if (!ReferenceEquals(_encoder, encoder))
        {
            throw new InvalidOperationException("Encoder no longer belongs to this record-only scope.");
        }
    }

    private void End(object encoder, Action end)
    {
        Verify(encoder);
        end();
        _end = null;
        _encoder = null;
    }

    private sealed class Render(RecordCommandScope owner, IRenderEncoder inner) : IRenderEncoder
    {
        public void SetArgumentTable(IArgumentTable table)
        {
            owner.Verify(this);
            inner.SetArgumentTable(table);
        }

        public void SetArgumentValidation(Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare)
        {
            owner.Verify(this);
            inner.SetArgumentValidation(prepare);
        }

        public void SetArguments<TArguments>(in TArguments arguments)
            where TArguments : struct, IShaderArguments
        {
            owner.Verify(this);
            inner.SetArguments<TArguments>(in arguments);
        }

        public void SetArguments<TArguments>(in TArguments arguments, string rootParameter = "arguments", string? shaderTypeName = null)
            where TArguments : unmanaged
        {
            owner.Verify(this);
            inner.SetArguments<TArguments>(in arguments, rootParameter, shaderTypeName);
        }

        public void SetPipeline(IGraphicsPipeline pipeline)
        {
            owner.Verify(this);
            inner.SetPipeline(pipeline);
        }

        public void SetPipeline(IMeshPipeline pipeline)
        {
            owner.Verify(this);
            inner.SetPipeline(pipeline);
        }

        public void DrawMeshTasks(uint x, uint y = 1, uint z = 1)
        {
            owner.Verify(this);
            inner.DrawMeshTasks(x, y, z);
        }

        public void SetRenderState(GraphicsRenderStateDesc state)
        {
            owner.Verify(this);
            inner.SetRenderState(state);
        }

        public void SetViewport(Viewport viewport)
        {
            owner.Verify(this);
            inner.SetViewport(viewport);
        }

        public void SetScissor(ScissorRect scissor)
        {
            owner.Verify(this);
            inner.SetScissor(scissor);
        }

        public void SetBlendConstant(BlendConstant value)
        {
            owner.Verify(this);
            inner.SetBlendConstant(value);
        }

        public void SetStencilReference(uint reference)
        {
            owner.Verify(this);
            inner.SetStencilReference(reference);
        }

        public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
        {
            owner.Verify(this);
            inner.Draw(vertexCount, instanceCount, firstVertex, firstInstance);
        }

        public void SetIndexBuffer(BufferSlice<ushort> indices)
        {
            owner.Verify(this);
            inner.SetIndexBuffer(indices);
        }

        public void SetIndexBuffer(BufferSlice<uint> indices)
        {
            owner.Verify(this);
            inner.SetIndexBuffer(indices);
        }

        public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0)
        {
            owner.Verify(this);
            inner.DrawIndexed(indexCount, instanceCount, firstIndex, baseVertex, firstInstance);
        }

        public void DrawIndirect(BufferSlice<DrawIndirectArguments> arguments)
        {
            owner.Verify(this);
            inner.DrawIndirect(arguments);
        }

        public void DrawIndexedIndirect(BufferSlice<DrawIndexedIndirectArguments> arguments)
        {
            owner.Verify(this);
            inner.DrawIndexedIndirect(arguments);
        }

        public void End() => owner.End(this, inner.End);
    }

    private sealed class Compute(RecordCommandScope owner, IComputeEncoder inner) : IComputeEncoder
    {
        public void SetArgumentTable(IArgumentTable table)
        {
            owner.Verify(this);
            inner.SetArgumentTable(table);
        }

        public void SetArgumentValidation(Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare)
        {
            owner.Verify(this);
            inner.SetArgumentValidation(prepare);
        }

        public void SetArguments<TArguments>(in TArguments arguments)
            where TArguments : struct, IShaderArguments
        {
            owner.Verify(this);
            inner.SetArguments<TArguments>(in arguments);
        }

        public void SetArguments<TArguments>(in TArguments arguments, string rootParameter = "arguments", string? shaderTypeName = null)
            where TArguments : unmanaged
        {
            owner.Verify(this);
            inner.SetArguments<TArguments>(in arguments, rootParameter, shaderTypeName);
        }

        public void SetPipeline(IGraphicsComputePipeline pipeline)
        {
            owner.Verify(this);
            inner.SetPipeline(pipeline);
        }

        public void Dispatch(uint groupCountX, uint groupCountY = 1, uint groupCountZ = 1)
        {
            owner.Verify(this);
            inner.Dispatch(groupCountX, groupCountY, groupCountZ);
        }

        public void DispatchIndirect(BufferSlice<DispatchIndirectArguments> arguments)
        {
            owner.Verify(this);
            inner.DispatchIndirect(arguments);
        }

        public void End() => owner.End(this, inner.End);
    }
}
