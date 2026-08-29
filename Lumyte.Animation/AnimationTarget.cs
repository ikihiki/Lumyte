namespace Lumyte.Animation;

public sealed class AnimationTarget
{
    private readonly Dictionary<AnimationChannel, IBinding> bindings = [];

    public AnimationTarget Bind<T>(AnimationChannel<T> channel, Action<T> apply)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(apply);
        bindings[channel] = new Binding<T>(channel, apply);
        return this;
    }

    public bool IsBound(AnimationChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        return bindings.ContainsKey(channel);
    }

    internal void Validate(IAnimationTimeline timeline)
    {
        AnimationChannel? missing = timeline.Channels.FirstOrDefault(channel => !bindings.ContainsKey(channel));
        if (missing is not null)
        {
            throw new InvalidOperationException(
                $"Animation channel '{missing.Name}' is not bound to the target.");
        }
    }

    internal void Apply(AnimationSample sample)
    {
        foreach (AnimationChannel channel in sample.Timeline.Channels)
        {
            bindings[channel].Apply(sample);
        }
    }

    private interface IBinding
    {
        void Apply(AnimationSample sample);
    }

    private sealed class Binding<T>(AnimationChannel<T> channel, Action<T> apply) : IBinding
    {
        public void Apply(AnimationSample sample) => apply(sample.Get(channel));
    }
}
