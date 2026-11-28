using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning.Tests;

/// <summary>Provides deterministic managed test instrumentation.</summary>
internal sealed class ManagedKernel : IGraphicsComputePipeline, IGraphicsPipeline
{
    ComputePipelineDesc IGraphicsComputePipeline.Desc => throw new NotSupportedException();

    GraphicsPipelineDesc IGraphicsPipeline.Desc => throw new NotSupportedException();

    public void Dispose()
    {
    }
}
