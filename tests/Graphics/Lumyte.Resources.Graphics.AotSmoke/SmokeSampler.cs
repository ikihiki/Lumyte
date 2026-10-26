using Lumyte.Graphics.Abstractions;

namespace Lumyte.Resources.Graphics.AotSmoke;

internal sealed class SmokeSampler(SamplerDesc desc) : IGraphicsSampler
{
    public SamplerDesc Desc { get; } = desc;

    internal bool Disposed { get; private set; }

    public void Dispose() => Disposed = true;
}
