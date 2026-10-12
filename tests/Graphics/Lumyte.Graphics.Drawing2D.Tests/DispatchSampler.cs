using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D.Tests;

internal sealed class DispatchSampler : IGraphicsSampler
{
    public SamplerDesc Desc { get; } = new();

    public void Dispose()
    {
    }
}
