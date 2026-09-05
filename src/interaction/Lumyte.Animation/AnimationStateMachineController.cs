using Lumyte.Core.Time;
using Lumyte.StateMachine;

namespace Lumyte.Animation;

public sealed class AnimationStateMachineController<TContext, TTrigger>
{
    private readonly StateMachineInstance<TContext, TTrigger> machine;
    private readonly AnimationStateMachineBinding<TContext, TTrigger> binding;
    private readonly AnimationPlayer player;
    private readonly AnimationTarget target;
    private PlaybackHandle? playback;
    private PlaybackOptions statePlaybackOptions = new()
    {
        LoopMode = PlaybackLoopMode.Repeat,
    };

    public AnimationStateMachineController(
        StateMachineInstance<TContext, TTrigger> machine,
        AnimationStateMachineBinding<TContext, TTrigger> binding,
        AnimationPlayer player,
        AnimationTarget target)
    {
        this.machine = machine ?? throw new ArgumentNullException(nameof(machine));
        this.binding = binding ?? throw new ArgumentNullException(nameof(binding));
        this.player = player ?? throw new ArgumentNullException(nameof(player));
        this.target = target ?? throw new ArgumentNullException(nameof(target));
        machine.Transitioned += OnTransitioned;
    }

    public State<TContext> CurrentState => machine.CurrentState;

    public PlaybackHandle Start(PlaybackOptions? options = null)
    {
        statePlaybackOptions = options ?? new PlaybackOptions
        {
            LoopMode = PlaybackLoopMode.Repeat,
        };
        playback?.Stop();
        playback = player.Play(binding.TimelineFor(CurrentState), target, statePlaybackOptions);
        return playback;
    }

    public bool Fire(TTrigger trigger) => machine.Fire(trigger);

    private void OnTransitioned(Transition<TContext, TTrigger> transition)
    {
        AnimationSample from = CurrentSample(transition.From);
        playback?.Stop();
        AnimationTransition animation = binding.TransitionFor(transition);
        IAnimationTimeline destination = binding.TimelineFor(transition.To);
        if (animation.Duration == Duration.Zero)
        {
            playback = player.Play(destination, target, statePlaybackOptions);
            return;
        }

        var crossfade = new CrossfadeTimeline(
            from,
            destination,
            animation.Duration,
            animation.Blend!,
            animation.Curve);
        PlaybackHandle transitionPlayback = player.Play(crossfade, target);
        transitionPlayback.Completed += CompleteTransition;
        playback = transitionPlayback;
    }

    private AnimationSample CurrentSample(State<TContext> previous)
    {
        IAnimationTimeline timeline = binding.TimelineFor(previous);
        return playback is null
            ? timeline.Sample(Duration.Zero)
            : playback.Timeline.Sample(playback.Position);
    }

    private void CompleteTransition(PlaybackHandle completed)
    {
        completed.Completed -= CompleteTransition;
        playback = player.Play(binding.TimelineFor(CurrentState), target, statePlaybackOptions);
        Duration continuation = completed.Timeline.Duration;
        if (continuation > Duration.Zero)
        {
            playback.Seek(continuation);
        }
    }
}
