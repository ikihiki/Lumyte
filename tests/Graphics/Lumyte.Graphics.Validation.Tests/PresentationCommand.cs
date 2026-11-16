using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation.Tests;

internal sealed class PresentationCommand : IGraphicsCommandBuffer
{
    public CommandBufferState State { get; private set; } = CommandBufferState.Recording;

    public void CopyBuffer<TSource, TDestination>(BufferSlice<TSource> source, BufferSlice<TDestination> destination)
        where TSource : unmanaged
        where TDestination : unmanaged => throw new NotSupportedException();

    public void CopyBuffer<T>(ShaderDataSlice<T> source, ShaderDataSlice<T> destination)
        where T : struct, IShaderArguments => throw new NotSupportedException();

    public void CopyTexture(TextureCopyRegion source, TextureCopyRegion destination) => throw new NotSupportedException();

    public void CopyBufferToTexture(BufferTextureCopyLayout source, TextureCopyRegion destination) => throw new NotSupportedException();

    public void CopyTextureToBuffer(TextureCopyRegion source, BufferTextureCopyLayout destination) => throw new NotSupportedException();

    public void Barrier(MemoryBarrierDesc barrier) => throw new NotSupportedException();

    public void Barrier<T>(BufferBarrierDesc<T> barrier)
        where T : unmanaged => throw new NotSupportedException();

    public void Barrier<T>(ShaderDataBufferBarrierDesc<T> barrier)
        where T : struct, IShaderArguments => throw new NotSupportedException();

    public void Barrier(TextureBarrierDesc barrier)
    {
    }

    public IRenderEncoder BeginRenderPass(RenderPassDesc desc) => throw new NotSupportedException();

    public IComputeEncoder BeginComputePass(ComputePassDesc desc) => throw new NotSupportedException();

    public void Finish() => State = CommandBufferState.Executable;

    public void Dispose() => State = CommandBufferState.Disposed;
}
