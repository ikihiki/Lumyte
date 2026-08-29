using Lumyte.Core.Time;
using Lumyte.Composition;

namespace Lumyte.Animation;

[Composable(Factory = "AnimationKit", Name = "Track")]
public sealed partial class AnimationTrack<T> : AnimationTrack
{
    private AnimationChannel<T> channel = null!;
    private IInterpolator<T> interpolator = null!;
    private ICurve curve = Curves.Linear;
    private Keyframe<T>[] keyframes = [];

    [ComposeParameter]
    public required AnimationChannel<T> Channel
    {
        get => channel;
        init
        {
            channel = value ?? throw new ArgumentNullException(nameof(value));
        }
    }

    public override AnimationChannel UntypedChannel => Channel;

    [ComposeParameter]
    public required IInterpolator<T> Interpolator
    {
        get => interpolator;
        init => interpolator = value ?? throw new ArgumentNullException(nameof(value));
    }

    [ComposeParameter]
    private ICurve Curve
    {
        get => curve;
        init => curve = value ?? throw new ArgumentNullException(nameof(value));
    }

    [ComposeContent]
    private IReadOnlyList<Keyframe<T>> ComposedKeyframes
    {
        get => keyframes;
        set
        {
            Keyframe<T>[] candidate = [.. value];
            Validate(candidate);
            keyframes = candidate;
        }
    }

    public IReadOnlyList<Keyframe<T>> Keyframes => keyframes;

    public override Duration Duration => keyframes.Length == 0 ? Duration.Zero : keyframes[^1].Time;

    public override Type ValueType => typeof(T);

    public T Sample(Duration time)
    {
        if (keyframes.Length == 0)
        {
            throw new InvalidOperationException($"Animation track '{Name}' has no keyframes.");
        }

        if (time <= keyframes[0].Time)
        {
            return keyframes[0].Value;
        }

        if (time >= keyframes[^1].Time)
        {
            return keyframes[^1].Value;
        }

        var upperIndex = FindUpperKeyframe(time);
        var lower = keyframes[upperIndex - 1];
        var upper = keyframes[upperIndex];
        var elapsedTicks = time.Ticks - lower.Time.Ticks;
        var spanTicks = upper.Time.Ticks - lower.Time.Ticks;
        var progress = Curve.Transform((float)elapsedTicks / spanTicks);
        return Interpolator.Interpolate(lower.Value, upper.Value, progress);
    }

    internal override object? SampleObject(Duration time) => Sample(time);

    internal override void SampleInto(Duration time, AnimationSampleBuffer buffer) =>
        buffer.Set(Channel, Sample(time));

    private int FindUpperKeyframe(Duration time)
    {
        var lower = 1;
        var upper = keyframes.Length - 1;
        while (lower < upper)
        {
            var middle = lower + ((upper - lower) / 2);
            if (keyframes[middle].Time <= time)
            {
                lower = middle + 1;
            }
            else
            {
                upper = middle;
            }
        }

        return lower;
    }

    private static void Validate(Keyframe<T>[] keyframes)
    {
        for (var index = 0; index < keyframes.Length; index++)
        {
            if (keyframes[index].Time < Duration.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(keyframes), "Keyframe times cannot be negative.");
            }

            if (index > 0 && keyframes[index].Time <= keyframes[index - 1].Time)
            {
                throw new ArgumentException("Keyframe times must be strictly increasing.", nameof(keyframes));
            }
        }
    }
}
