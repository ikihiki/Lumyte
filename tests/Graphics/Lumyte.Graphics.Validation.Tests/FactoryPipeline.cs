using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation.Tests;

internal sealed class FactoryPipeline(GraphicsPipelineDesc desc) : IGraphicsPipeline
{
    public GraphicsPipelineDesc Desc => desc;

    public void Dispose()
    {
    }
}
