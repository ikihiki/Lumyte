using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Incremental.Benchmarks;

// Forward every public command to the real backend. Counts are issued abstraction
// draw calls (including indirect), not GPU primitive counts or cache estimates.
internal sealed class PublicRetainedApiProbeCommands(IGraphicsCommandBuffer inner) : IGraphicsCommandBuffer
{
    public CommandBufferState State => inner.State;

    internal IGraphicsCommandBuffer Inner => inner;

    internal int DrawCalls { get; private set; }

    public void CopyBuffer<TSource, TDestination>(BufferSlice<TSource> source, BufferSlice<TDestination> destination)
        where TSource : unmanaged
        where TDestination : unmanaged
        => inner.CopyBuffer(source, destination);

    public void CopyTexture(TextureCopyRegion source, TextureCopyRegion destination) => inner.CopyTexture(source, destination);

    public void CopyBufferToTexture(BufferTextureCopyLayout source, TextureCopyRegion destination) => inner.CopyBufferToTexture(source, destination);

    public void CopyTextureToBuffer(TextureCopyRegion source, BufferTextureCopyLayout destination) => inner.CopyTextureToBuffer(source, destination);

    public void Barrier(MemoryBarrierDesc barrier) => inner.Barrier(barrier);

    public void Barrier<T>(BufferBarrierDesc<T> barrier)
        where T : unmanaged
        => inner.Barrier(barrier);

    public void Barrier(TextureBarrierDesc barrier) => inner.Barrier(barrier);

    public IRenderEncoder BeginRenderPass(RenderPassDesc desc) => new Counter(inner.BeginRenderPass(desc), this);

    public IComputeEncoder BeginComputePass(ComputePassDesc desc) => inner.BeginComputePass(desc);

    public void Finish() => inner.Finish();

    public void CopyBuffer<T>(ShaderDataSlice<T> source, ShaderDataSlice<T> destination)
        where T : IShaderData => inner.CopyBuffer(source, destination);

    public void Barrier<T>(ShaderDataBufferBarrierDesc<T> barrier)
        where T : IShaderData => inner.Barrier(barrier);

    public void Dispose() => inner.Dispose();

    private sealed class Counter(IRenderEncoder inner, PublicRetainedApiProbeCommands owner) : IRenderEncoder
    {
        public void SetArgumentTable(IArgumentTable table) => inner.SetArgumentTable(table);

        public void SetArguments<T>(in T arguments)
            where T : IShaderArguments
            => inner.SetArguments(in arguments);

        public void SetPipeline(IGraphicsPipeline pipeline) => inner.SetPipeline(pipeline);

        public void SetRenderState(GraphicsRenderStateDesc state) => inner.SetRenderState(state);

        public void SetViewport(Viewport viewport) => inner.SetViewport(viewport);

        public void SetScissor(ScissorRect scissor) => inner.SetScissor(scissor);

        public void SetBlendConstant(BlendConstant value) => inner.SetBlendConstant(value);

        public void SetStencilReference(uint reference) => inner.SetStencilReference(reference);

        public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
        {
            inner.Draw(vertexCount, instanceCount, firstVertex, firstInstance);
            owner.DrawCalls++;
        }

        public void SetIndexBuffer(BufferSlice<ushort> indices) => inner.SetIndexBuffer(indices);

        public void SetIndexBuffer(BufferSlice<uint> indices) => inner.SetIndexBuffer(indices);

        public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0)
        {
            inner.DrawIndexed(indexCount, instanceCount, firstIndex, baseVertex, firstInstance);
            owner.DrawCalls++;
        }

        public void DrawIndirect(BufferSlice<DrawIndirectArguments> arguments)
        {
            inner.DrawIndirect(arguments);
            owner.DrawCalls++;
        }

        public void DrawIndexedIndirect(BufferSlice<DrawIndexedIndirectArguments> arguments)
        {
            inner.DrawIndexedIndirect(arguments);
            owner.DrawCalls++;
        }

        public void End() => inner.End();
    }
}
