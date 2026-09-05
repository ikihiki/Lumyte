using Lumyte.Core.Time;

namespace Lumyte.Animation;

public abstract class AnimationTrack
{
    public string Name => UntypedChannel.Name;

    public abstract AnimationChannel UntypedChannel { get; }

    public abstract Duration Duration { get; }

    public abstract Type ValueType { get; }

    internal abstract object? SampleObject(Duration time);

    internal abstract void SampleInto(Duration time, AnimationSampleBuffer buffer);
}
