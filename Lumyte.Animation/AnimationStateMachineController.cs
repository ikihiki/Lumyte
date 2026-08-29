using Lumyte.Core.Time;

namespace Lumyte.Animation;

public sealed class AnimationStateMachineController
{
    private readonly AnimationStateMachine machine;
    private readonly AnimationPlayer player;
    private readonly AnimationTarget target;
    private PlaybackHandle? playback;
    private PlaybackOptions statePlaybackOptions = new()
    {
        LoopMode = PlaybackLoopMode.Repeat,
    };

    public AnimationStateMachineController(
        AnimationStateMachine machine,
        AnimationPlayer player,
        AnimationTarget target)
    {
        this.machine = machine ?? throw new ArgumentNullException(nameof(machine));
        this.player = player ?? throw new ArgumentNullException(nameof(player));
        this.target = target ?? throw new ArgumentNullException(nameof(target));
        CurrentState = machine.InitialState;
    }

    public AnimationState CurrentState { get; private set; }

    public PlaybackHandle Start(PlaybackOptions? options = null)
    {
        statePlaybackOptions = options ?? new PlaybackOptions
        {
            LoopMode = PlaybackLoopMode.Repeat,
        };
        playback?.Stop();
        playback = player.Play(CurrentState.Timeline, target, statePlaybackOptions);
        return playback;
    }

    public bool Fire(string trigger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trigger);
        AnimationTransition? transition = machine.FindTransition(CurrentState, trigger);
        if (transition is null)
        {
            return false;
        }

        AnimationSample from = CurrentSample();
        playback?.Stop();
        CurrentState = transition.To;
        if (transition.Duration == Duration.Zero)
        {
            playback = player.Play(CurrentState.Timeline, target, statePlaybackOptions);
            return true;
        }

        var crossfade = new CrossfadeTimeline(
            from,
            CurrentState.Timeline,
            transition.Duration,
            transition.Blend!,
            transition.Curve);
        PlaybackHandle transitionPlayback = player.Play(crossfade, target);
        transitionPlayback.Completed += CompleteTransition;
        playback = transitionPlayback;
        return true;
    }

    private AnimationSample CurrentSample()
    {
        if (playback is null)
        {
            return CurrentState.Timeline.Sample(Duration.Zero);
        }

        return playback.Timeline.Sample(playback.Position);
    }

    private void CompleteTransition(PlaybackHandle completed)
    {
        completed.Completed -= CompleteTransition;
        playback = player.Play(CurrentState.Timeline, target, statePlaybackOptions);
        Duration continuation = completed.Timeline.Duration;
        if (continuation > Duration.Zero)
        {
            playback.Seek(continuation);
        }
    }
}
