using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;

namespace Lumyte.Graphics.RenderGraph.Tests;

internal sealed class PublicGraphConsumerCommands : IGraphicsCommandBuffer
{
    private readonly ShaderDataTransferState _transfers = new();

    public CommandBufferState State
    {
        get; private set;
    }

    internal PublicGraphConsumerEncoder Encoder { get; init; } = new();

    internal int EndAttempts => Encoder.Ends;

    internal List<object> Events { get; } = [];

    public void Barrier(MemoryBarrierDesc barrier) => Events.Add(barrier);

    public void Barrier<T>(BufferBarrierDesc<T> barrier)
        where T : unmanaged => Events.Add(barrier);

    public void Barrier(TextureBarrierDesc barrier) => Events.Add(barrier);

    public void Barrier<T>(ShaderDataBufferBarrierDesc<T> barrier)
        where T : IShaderData => Events.Add(barrier);

    public void CopyBuffer<TSource, TDestination>(BufferSlice<TSource> source, BufferSlice<TDestination> destination)
        where TSource : unmanaged
        where TDestination : unmanaged => Events.Add("copy");

    public void CopyBuffer<T>(ShaderDataSlice<T> source, ShaderDataSlice<T> destination)
        where T : IShaderData => _transfers.Record((IShaderDataSource)source.Buffer, source.Offset, (IShaderDataSource)destination.Buffer, destination.Offset, source.Count, () => Events.Add("shader-copy"));

    public void CopyTexture(TextureCopyRegion source, TextureCopyRegion destination) => Events.Add("texture-copy");

    public void CopyBufferToTexture(BufferTextureCopyLayout source, TextureCopyRegion destination) => throw new NotSupportedException();

    public void CopyTextureToBuffer(TextureCopyRegion source, BufferTextureCopyLayout destination) => throw new NotSupportedException();

    public IRenderEncoder BeginRenderPass(RenderPassDesc desc)
    {
        Encoder.ReadShaderData = _transfers.Read;
        return Encoder;
    }

    public IComputeEncoder BeginComputePass(ComputePassDesc desc)
    {
        Encoder.ReadShaderData = _transfers.Read;
        return Encoder;
    }

    public void Finish() => State = CommandBufferState.Executable;

    public void Dispose() => State = CommandBufferState.Disposed;

    internal void Complete() => State = CommandBufferState.Completed;

    internal void Submit() => State = CommandBufferState.Submitted;
}
