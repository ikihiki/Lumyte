using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D.Tests;

internal sealed class ProfilingCommands : IGraphicsCommandBuffer
{
    public CommandBufferState State { get; private set; } = CommandBufferState.Recording;

    internal List<object> Events { get; } = [];

    public void Barrier(MemoryBarrierDesc barrier) => Events.Add(barrier);

    public void Barrier<T>(BufferBarrierDesc<T> barrier)
        where T : unmanaged => Events.Add(barrier);

    public void Barrier(TextureBarrierDesc barrier) => Events.Add(barrier);

    public void Barrier<T>(ShaderDataBufferBarrierDesc<T> barrier)
        where T : struct, IShaderData => Events.Add(barrier);

    public void CopyBuffer<TSource, TDestination>(BufferSlice<TSource> source, BufferSlice<TDestination> destination)
        where TSource : unmanaged
        where TDestination : unmanaged => Events.Add("copy");

    public void CopyBuffer<T>(ShaderDataSlice<T> source, ShaderDataSlice<T> destination)
        where T : struct, IShaderData => Events.Add("shader-copy");

    public void CopyTexture(TextureCopyRegion source, TextureCopyRegion destination) => Events.Add("texture-copy");

    public void CopyBufferToTexture(BufferTextureCopyLayout source, TextureCopyRegion destination) => throw new NotSupportedException();

    public void CopyTextureToBuffer(TextureCopyRegion source, BufferTextureCopyLayout destination) => throw new NotSupportedException();

    public IRenderEncoder BeginRenderPass(RenderPassDesc desc) => new Encoder(this);

    public IComputeEncoder BeginComputePass(ComputePassDesc desc) => new ComputeEncoder(this);

    public void Finish() => State = CommandBufferState.Executable;

    public void Dispose() => State = CommandBufferState.Disposed;

    internal void Complete() => State = CommandBufferState.Completed;

    internal void Submit() => State = CommandBufferState.Submitted;

    private sealed class Encoder(ProfilingCommands owner) : IRenderEncoder
    {
        public void SetArgumentTable(IArgumentTable table) => owner.Events.Add(table);

        public void SetArguments(ShaderValueSnapshot arguments) => owner.Events.Add(arguments);

        public void SetArguments<T>(in T arguments)
            where T : struct, IShaderArguments => owner.Events.Add(arguments);

        public void SetPipeline(IGraphicsPipeline pipeline)
        {
        }

        public void SetRenderState(GraphicsRenderStateDesc state)
        {
        }

        public void SetViewport(Viewport viewport)
        {
        }

        public void SetScissor(ScissorRect scissor) => owner.Events.Add(scissor);

        public void SetBlendConstant(BlendConstant value)
        {
        }

        public void SetStencilReference(uint reference)
        {
        }

        public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0) => owner.Events.Add((vertexCount, instanceCount));

        public void SetIndexBuffer(BufferSlice<ushort> indices)
        {
        }

        public void SetIndexBuffer(BufferSlice<uint> indices) => throw new NotSupportedException();

        public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0) => owner.Events.Add(("indexed", indexCount, instanceCount, firstIndex, baseVertex, firstInstance));

        public void DrawIndirect(BufferSlice<DrawIndirectArguments> arguments) => throw new NotSupportedException();

        public void DrawIndexedIndirect(BufferSlice<DrawIndexedIndirectArguments> arguments) => throw new NotSupportedException();

        public void End()
        {
        }
    }

    private sealed class ComputeEncoder(ProfilingCommands owner) : IComputeEncoder
    {
        public void SetArgumentTable(IArgumentTable table) => owner.Events.Add(table);

        public void SetArguments(ShaderValueSnapshot arguments) => owner.Events.Add(arguments);

        public void SetArguments<T>(in T arguments)
            where T : struct, IShaderArguments => owner.Events.Add(arguments);

        public void SetPipeline(IGraphicsComputePipeline pipeline)
        {
        }

        public void Dispatch(uint groupCountX, uint groupCountY = 1, uint groupCountZ = 1) => owner.Events.Add(("dispatch", groupCountX, groupCountY, groupCountZ));

        public void DispatchIndirect(BufferSlice<DispatchIndirectArguments> arguments) => throw new NotSupportedException();

        public void End()
        {
        }
    }
}
