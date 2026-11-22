using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Pipelines;

internal sealed class PipelineSamplerResource(IGraphicDevice device, GraphicsDeviceGeneration generation, IGraphicsSampler sampler, BarrierScope scope)
    : PipelineExternalResource(device, generation, sampler, scope)
{
    internal IGraphicsSampler Sampler { get; } = sampler;

    internal override PipelineExternalResource Snapshot() => new PipelineSamplerResource(Device, Generation, Sampler, Scope) { Order = Order };
}
