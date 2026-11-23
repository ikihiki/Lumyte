using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Pipelines;

internal sealed class PipelineBufferResource<TElement>(IGraphicDevice device, GraphicsDeviceGeneration generation, BufferSlice<TElement> slice, BarrierScope scope)
    : PipelineExternalResource(device, generation, slice.Buffer, scope)
    where TElement : unmanaged
{
    internal BufferSlice<TElement> Slice { get; } = slice;

    internal override PipelineExternalResource Snapshot() => new PipelineBufferResource<TElement>(Device, Generation, Slice, Scope) { Order = Order };
}
