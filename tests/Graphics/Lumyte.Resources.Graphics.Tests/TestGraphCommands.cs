using Lumyte.Graphics.Abstractions;

namespace Lumyte.Resources.Graphics.Tests;

internal sealed class TestGraphCommands : IGraphicsCommandBuffer
{
    public CommandBufferState State { get; private set; }

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
        where T : struct, IShaderData => throw new NotSupportedException();

    public void CopyTexture(TextureCopyRegion source, TextureCopyRegion destination) => Events.Add("texture-copy");

    public void CopyBufferToTexture(BufferTextureCopyLayout source, TextureCopyRegion destination) => throw new NotSupportedException();

    public void CopyTextureToBuffer(TextureCopyRegion source, BufferTextureCopyLayout destination) => throw new NotSupportedException();

    public IRenderEncoder BeginRenderPass(RenderPassDesc desc) => throw new NotSupportedException();

    public IComputeEncoder BeginComputePass(ComputePassDesc desc) => throw new NotSupportedException();

    public void Finish() => State = CommandBufferState.Executable;

    public void Dispose() => State = CommandBufferState.Disposed;

    internal void Complete() => State = CommandBufferState.Completed;

    internal void Submit() => State = CommandBufferState.Submitted;
}
