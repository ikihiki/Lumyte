using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Benchmarks.Testing;

internal sealed class CpuComputePipeline(CpuGraphicsDevice owner, ComputePipelineDesc desc) : CpuResource(owner), IGraphicsComputePipeline
{
    public ComputePipelineDesc Desc => desc;
}
