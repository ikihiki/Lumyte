using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Benchmarks.Testing;

internal sealed class CpuPipeline(CpuGraphicsDevice owner, GraphicsPipelineDesc desc) : CpuResource(owner), IGraphicsPipeline
{
    public GraphicsPipelineDesc Desc => desc;
}
