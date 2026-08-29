using Lumyte.Core.Time;

namespace Lumyte.Animation;

public abstract class AnimationTrack
{
    public abstract string Name { get; init; }

    public abstract Duration Duration { get; }

    public abstract Type ValueType { get; }

    internal abstract object? SampleObject(Duration time);
}
