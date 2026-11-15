using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation.Tests;

internal sealed class TypedSampler(SamplerDesc desc) : IGraphicsSampler
{
    public SamplerDesc Desc => desc;

    public void Dispose()
    {
    }
}
