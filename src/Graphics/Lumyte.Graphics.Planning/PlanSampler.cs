using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

/// <summary>A borrowed immutable sampler authority retained by its device session.</summary>
public sealed class PlanSampler
{
    internal PlanSampler(GraphicsDeviceSession session, IGraphicsSampler sampler)
    {
        (Session, Sampler) = (session, sampler);
    }

    internal GraphicsDeviceSession Session { get; }

    internal IGraphicsSampler Sampler { get; }
}
