namespace Lumyte.Animation;

public sealed record AnimationState
{
    public AnimationState(string name, IAnimationTimeline timeline)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Timeline = timeline ?? throw new ArgumentNullException(nameof(timeline));
    }

    public string Name { get; }

    public IAnimationTimeline Timeline { get; }
}
