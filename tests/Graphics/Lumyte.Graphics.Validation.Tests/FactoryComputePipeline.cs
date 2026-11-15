using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation.Tests;

internal sealed class FactoryComputePipeline(ComputePipelineDesc desc) : IGraphicsComputePipeline
{
    public ComputePipelineDesc Desc => desc;

    public void Dispose()
    {
    }
}
