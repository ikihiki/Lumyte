using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Benchmarks.Testing;

internal sealed class CpuSampler(CpuGraphicsDevice owner, SamplerDesc desc) : CpuResource(owner), IGraphicsSampler
{
    public SamplerDesc Desc => desc;
}
