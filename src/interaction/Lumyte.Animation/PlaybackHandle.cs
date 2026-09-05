using Lumyte.Core.Time;

namespace Lumyte.Animation;

public sealed class PlaybackHandle
{
    private readonly IMonotonicClock clock;
    private readonly AnimationTarget target;
    private readonly AnimationSampleBuffer sampleBuffer;
    private TimePoint anchorClock;
    private Duration anchorPosition;
    private Duration remainingDelay;
    private Duration lastAppliedTime;
    private bool hasApplied;
    private bool resumeScheduled;
    private bool completionRaised;

    internal PlaybackHandle(
        IMonotonicClock clock,
        IAnimationTimeline timeline,
        AnimationTarget target,
        PlaybackOptions options)
    {
        this.clock = clock;
        this.target = target;
        Timeline = timeline;
        sampleBuffer = new(timeline);
        Options = options;
        anchorClock = clock.Now + options.Delay;
        State = options.Delay > Duration.Zero ? PlaybackState.Scheduled : PlaybackState.Playing;
    }

    public event Action<PlaybackHandle>? Completed;

    public IAnimationTimeline Timeline { get; }

    public AnimationClip? Clip => Timeline as AnimationClip;

    public PlaybackOptions Options { get; }

    public PlaybackState State { get; private set; }

    public Duration Position { get; private set; }

    internal bool IsTerminal => State is PlaybackState.Completed or PlaybackState.Stopped;

    public void Pause()
    {
        if (State is PlaybackState.Paused or PlaybackState.Completed or PlaybackState.Stopped)
        {
            return;
        }

        TimePoint now = clock.Now;
        if (State == PlaybackState.Scheduled)
        {
            if (now >= anchorClock)
            {
                Update(now);
                if (IsTerminal)
                {
                    return;
                }

                anchorPosition = TimelinePosition(now);
                anchorClock = now;
                resumeScheduled = false;
                State = PlaybackState.Paused;
                return;
            }

            remainingDelay = anchorClock - now;
            resumeScheduled = true;
        }
        else
        {
            anchorPosition = TimelinePosition(now);
            anchorClock = now;
            resumeScheduled = false;
        }

        State = PlaybackState.Paused;
    }

    public void Resume()
    {
        if (State != PlaybackState.Paused)
        {
            return;
        }

        if (resumeScheduled)
        {
            anchorClock = clock.Now + remainingDelay;
            State = PlaybackState.Scheduled;
        }
        else
        {
            anchorClock = clock.Now;
            State = PlaybackState.Playing;
        }
    }

    public void Seek(Duration position)
    {
        if (position < Duration.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(position), "Playback position cannot be negative.");
        }

        if (IsTerminal)
        {
            throw new InvalidOperationException("A completed or stopped playback cannot be seeked.");
        }

        anchorPosition = position;
        anchorClock = clock.Now;
        resumeScheduled = false;
        Apply(MapToClip(position));

        if (State == PlaybackState.Scheduled)
        {
            State = PlaybackState.Playing;
        }

        if (State == PlaybackState.Playing
            && Options.LoopMode == PlaybackLoopMode.Once
            && position >= Timeline.Duration)
        {
            Complete();
        }
    }

    public void Stop()
    {
        if (!IsTerminal)
        {
            State = PlaybackState.Stopped;
        }
    }

    internal void Start()
    {
        if (State == PlaybackState.Scheduled)
        {
            return;
        }

        Apply(Duration.Zero);
    }

    internal void Update(TimePoint now)
    {
        if (State is PlaybackState.Paused or PlaybackState.Completed or PlaybackState.Stopped)
        {
            return;
        }

        if (State == PlaybackState.Scheduled)
        {
            if (now < anchorClock)
            {
                return;
            }

            State = PlaybackState.Playing;
            Apply(Duration.Zero);
        }

        if (Timeline.Duration == Duration.Zero)
        {
            Complete();
            return;
        }

        Duration timeline = TimelinePosition(now);
        if (Options.LoopMode == PlaybackLoopMode.Once && timeline >= Timeline.Duration)
        {
            Apply(Timeline.Duration);
            Complete();
            return;
        }

        Apply(MapToClip(timeline));
    }

    private Duration TimelinePosition(TimePoint now)
    {
        Duration elapsed = now - anchorClock;
        long scaledTicks = checked((long)(elapsed.Ticks * Options.Speed));
        return anchorPosition + Duration.FromTicks(scaledTicks);
    }

    private Duration MapToClip(Duration timeline)
    {
        long durationTicks = Timeline.Duration.Ticks;
        if (durationTicks == 0 || Options.LoopMode == PlaybackLoopMode.Once)
        {
            return timeline >= Timeline.Duration ? Timeline.Duration : timeline;
        }

        if (Options.LoopMode == PlaybackLoopMode.Repeat)
        {
            return Duration.FromTicks(timeline.Ticks % durationTicks);
        }

        long cycleTicks = checked(durationTicks * 2);
        long cyclePosition = timeline.Ticks % cycleTicks;
        return Duration.FromTicks(
            cyclePosition <= durationTicks ? cyclePosition : cycleTicks - cyclePosition);
    }

    private void Apply(Duration time)
    {
        Position = time;
        if (hasApplied && time == lastAppliedTime)
        {
            return;
        }

        Timeline.SampleInto(time, sampleBuffer);
        target.Apply(sampleBuffer);
        lastAppliedTime = time;
        hasApplied = true;
    }

    private void Complete()
    {
        State = PlaybackState.Completed;
        if (!completionRaised)
        {
            completionRaised = true;
            Completed?.Invoke(this);
        }
    }
}
