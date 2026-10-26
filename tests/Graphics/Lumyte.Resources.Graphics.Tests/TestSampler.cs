using Lumyte.Graphics.Abstractions;

namespace Lumyte.Resources.Graphics.Tests;

internal sealed class TestSampler(SamplerDesc desc) : IGraphicsSampler
{
    public SamplerDesc Desc { get; } = desc;

    internal bool Disposed { get; private set; }

    public void Dispose() => Disposed = true;
}
