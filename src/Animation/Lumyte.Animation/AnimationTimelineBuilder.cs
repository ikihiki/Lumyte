using Lumyte.Core.Time;

namespace Lumyte.Animation;

/// <summary>Builds an immutable timeline from values, nested timelines and markers.</summary>
public sealed class AnimationTimelineBuilder
{
    private readonly List<PlacedNode> _items = [];

    private long? _duration;

    /// <summary>Registers a pure value source at a nonnegative typed start time.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="start">The start.</param>
    /// <param name="source">The source.</param>
    /// <param name="channel">The channel.</param>
    /// <param name="fill">The fill.</param>
    public void Add<T>(Duration start, IAnimationSource<T> source, AnimationChannel<T> channel, AnimationFillMode fill = AnimationFillMode.Hold)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start.Ticks, nameof(start));
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(source.Duration.Ticks, nameof(source));
        if (!Enum.IsDefined(fill))
        {
            throw new ArgumentOutOfRangeException(nameof(fill));
        }

        _ = checked(start.Ticks + source.Duration.Ticks);
        _items.Add(new PlacedNode(start.Ticks, new ValueNode<T>(source, channel, fill)));
    }

    /// <summary>Registers an immutable nested timeline at a nonnegative typed start time.</summary>
    /// <param name="start">The start.</param>
    /// <param name="timeline">The timeline.</param>
    public void AddTimeline(Duration start, AnimationTimeline timeline)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start.Ticks, nameof(start));
        ArgumentNullException.ThrowIfNull(timeline);
        _ = checked(start.Ticks + timeline.Duration.Ticks);
        _items.Add(new PlacedNode(start.Ticks, timeline.Root));
    }

    /// <summary>Registers a named marker at a nonnegative typed time.</summary>
    /// <param name="time">The time.</param>
    /// <param name="name">The name.</param>
    /// <param name="payload">The payload.</param>
    public void AddEvent(Duration time, string name, string? payload = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(time.Ticks, nameof(time));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _items.Add(new PlacedNode(time.Ticks, new MarkerNode(new AnimationEvent(Duration.Zero, name, payload))));
    }

    /// <summary>Sets a positive explicit timeline length, checked against item ends during Build.</summary>
    /// <param name="duration">The duration.</param>
    public void SetDuration(Duration duration)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(duration.Ticks, nameof(duration));
        _duration = duration.Ticks;
    }

    /// <summary>Validates and snapshots the definition without starting playback or applying values.</summary>
    /// <returns>The computed result.</returns>
    public AnimationTimeline Build()
    {
        long minimum = 0;
        foreach (PlacedNode item in _items)
        {
            minimum = Math.Max(minimum, checked(item.Start + item.Node.Length));
        }

        long length = _duration ?? minimum;
        if (length < minimum || length == 0)
        {
            throw new ArgumentException("The timeline needs a positive duration covering every item.");
        }

        return new AnimationTimeline(new GroupNode([.. _items], length));
    }
}
