using Lumyte.Core.Time;

namespace Lumyte.Animation;

/// <summary>An immutable timeline supporting compact finite repetition and reversed evaluation.</summary>
public sealed class AnimationTimeline
{
    internal AnimationTimeline(TimelineNode root)
    {
        Root = root;
    }

    /// <summary>Gets the duration.</summary>
    public Duration Duration => Duration.FromTicks(Root.Length);

    internal TimelineNode Root { get; }

    /// <summary>Creates a compact finite repetition with checked duration arithmetic.</summary>
    /// <param name="count">The count.</param>
    /// <returns>The computed result.</returns>
    public AnimationTimeline Repeat(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        return new AnimationTimeline(new RepeatNode(Root, count));
    }

    /// <summary>Creates a timeline evaluated at duration minus local time.</summary>
    /// <returns>The computed result.</returns>
    public AnimationTimeline Reverse() => new(new ReverseNode(Root));
}
