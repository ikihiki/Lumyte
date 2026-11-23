using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Observes actual native recording without replacing transfers or encoders.</summary>
public sealed partial class GenericGpuNativeTests
{
    private sealed class UploadObservationCommands(IGraphicsCommandBuffer native, List<(object Destination, ulong Bytes)> copies) : IGraphicsCommandBuffer, IGraphicsNativeOrigin
    {
        public CommandBufferState State => native.State;

        public IGraphicDevice Device => ((IGraphicsNativeOrigin)native).Device;

        public object AllocationIdentity => ((IGraphicsNativeOrigin)native).AllocationIdentity;

        public void ValidateOrigin() => ((IGraphicsNativeOrigin)native).ValidateOrigin();

        public void CopyBuffer<TSource, TDestination>(BufferSlice<TSource> source, BufferSlice<TDestination> destination)
            where TSource : unmanaged
            where TDestination : unmanaged
        {
            copies.Add((destination.Buffer, source.SizeInBytes));
            native.CopyBuffer(source, destination);
        }

        public void CopyBuffer<T>(ShaderDataSlice<T> source, ShaderDataSlice<T> destination)
            where T : struct, IShaderArguments => native.CopyBuffer(source, destination);

        public void CopyTexture(TextureCopyRegion source, TextureCopyRegion destination) => native.CopyTexture(source, destination);

        public void CopyBufferToTexture(BufferTextureCopyLayout source, TextureCopyRegion destination) => native.CopyBufferToTexture(source, destination);

        public void CopyTextureToBuffer(TextureCopyRegion source, BufferTextureCopyLayout destination) => native.CopyTextureToBuffer(source, destination);

        public void Barrier(MemoryBarrierDesc barrier) => native.Barrier(barrier);

        public void Barrier<T>(BufferBarrierDesc<T> barrier)
            where T : unmanaged => native.Barrier(barrier);

        public void Barrier<T>(ShaderDataBufferBarrierDesc<T> barrier)
            where T : struct, IShaderArguments => native.Barrier(barrier);

        public void Barrier(TextureBarrierDesc barrier) => native.Barrier(barrier);

        public IRenderEncoder BeginRenderPass(RenderPassDesc desc) => native.BeginRenderPass(desc);

        public IComputeEncoder BeginComputePass(ComputePassDesc desc) => native.BeginComputePass(desc);

        public void Finish() => native.Finish();

        public void Dispose() => native.Dispose();
    }
}
