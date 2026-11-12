using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph.Tests;

internal sealed class PublicGraphConsumerSampler(SamplerDesc desc) : IGraphicsSampler
{
    public SamplerDesc Desc => desc;

    public void Dispose()
    {
    }
}
