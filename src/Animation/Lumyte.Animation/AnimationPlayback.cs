using Lumyte.Core.Time;

namespace Lumyte.Animation;

/// <summary>Evaluates a timeline using an injected monotonic clock without applying values to targets.</summary>
public sealed class AnimationPlayback
{
    private readonly IMonotonicClock _clock;

    private readonly AnimationTimeline _timeline;

    private readonly AnimationWrapMode _wrapMode;

    private readonly EventCollector _events = new();

    private readonly List<TimingAnchor> _timing = [];

    private TimePoint _lastEvaluationClock;

    private TimePoint _lastObserved;

    private TimePoint _anchorClock;

    private long _anchorPosition;

    private long _lastEvaluation;

    private double _speed = 1;

    private bool _includeStart = true;

    /// <summary>Initializes a new instance of the <see cref="AnimationPlayback"/> class.</summary>
    /// <param name="clock">The clock.</param>
    /// <param name="timeline">The timeline.</param>
    /// <param name="wrapMode">The wrap mode.</param>
    public AnimationPlayback(IMonotonicClock clock, AnimationTimeline timeline, AnimationWrapMode wrapMode = AnimationWrapMode.Once)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(timeline);
        if (!Enum.IsDefined(wrapMode))
        {
            throw new ArgumentOutOfRangeException(nameof(wrapMode));
        }

        _clock = clock;
        _timeline = timeline;
        _wrapMode = wrapMode;
        _lastObserved = clock.Now;
        _anchorClock = _lastObserved;
    }

    /// <summary>Gets the current playback state.</summary>
    public AnimationPlaybackState State { get; private set; }

    /// <summary>Gets the last captured playback position.</summary>
    public Duration Position { get; private set; }

    /// <summary>Gets or sets the speed.</summary>
    public double Speed
    {
        get => _speed;
        set
        {
            if (!double.IsFinite(value) || value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            TimePoint now = ReadClock();
            long position = At(now);
            _anchorClock = now;
            _anchorPosition = position;
            Position = Map(position);
            _speed = value;
            _timing.Add(new TimingAnchor(position, now, State == AnimationPlaybackState.Playing ? value : 0));
        }
    }

    /// <summary>Starts or resumes playback; completed or cancelled playback restarts at zero.</summary>
    public void Play()
    {
        if (State == AnimationPlaybackState.Playing)
        {
            return;
        }

        if (State == AnimationPlaybackState.Paused)
        {
            Resume();
            return;
        }

        TimePoint now = ReadClock();
        _anchorPosition = 0;
        _lastEvaluation = 0;
        _anchorClock = now;
        _includeStart = true;
        _lastEvaluationClock = now;
        _timing.Clear();
        _timing.Add(new TimingAnchor(0, now, _speed));
        Position = Duration.Zero;
        State = AnimationPlaybackState.Playing;
    }

    /// <summary>Freezes the current position without applying any values.</summary>
    public void Pause()
    {
        if (State != AnimationPlaybackState.Playing)
        {
            return;
        }

        TimePoint now = ReadClock();
        _anchorPosition = At(now);
        _anchorClock = now;
        Position = Map(_anchorPosition);
        _timing.Add(new TimingAnchor(_anchorPosition, now, 0));
        State = AnimationPlaybackState.Paused;
    }

    /// <summary>Resumes paused playback from a new clock anchor.</summary>
    public void Resume()
    {
        if (State != AnimationPlaybackState.Paused)
        {
            return;
        }

        _anchorClock = ReadClock();
        _timing.Add(new TimingAnchor(_anchorPosition, _anchorClock, _speed));
        State = AnimationPlaybackState.Playing;
    }

    /// <summary>Stops playback and resets its position to zero without modifying consumer values.</summary>
    public void Stop()
    {
        _anchorPosition = 0;
        _lastEvaluation = 0;
        Position = Duration.Zero;
        _includeStart = true;
        State = AnimationPlaybackState.Stopped;
    }

    /// <summary>Changes the position and resets event traversal without applying or dispatching results.</summary>
    /// <param name="position">The position.</param>
    public void Seek(Duration position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position.Ticks, nameof(position));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(position.Ticks, _timeline.Duration.Ticks, nameof(position));
        TimePoint now = ReadClock();
        long mapped = _wrapMode == AnimationWrapMode.Loop && position == _timeline.Duration ? 0 : position.Ticks;
        _anchorClock = now;
        _anchorPosition = mapped;
        _lastEvaluation = mapped;
        _includeStart = false;
        _lastEvaluationClock = now;
        _timing.Clear();
        _timing.Add(new TimingAnchor(mapped, now, State == AnimationPlaybackState.Playing ? _speed : 0));
        Position = Duration.FromTicks(mapped);
    }

    /// <summary>Stops producing results while retaining the current position.</summary>
    public void Cancel()
    {
        TimePoint now = ReadClock();
        _anchorPosition = At(now);
        _anchorClock = now;
        Position = Map(_anchorPosition);
        State = AnimationPlaybackState.Cancelled;
    }

    /// <summary>Reads the clock once and appends computed values and crossed events without invoking target callbacks.</summary>
    /// <param name="output">The output.</param>
    /// <param name="events">The events.</param>
    /// <returns>The operation result.</returns>
    public bool Update(AnimationOutput output, ICollection<AnimationEventOccurrence> events)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(events);
        if (State is AnimationPlaybackState.Stopped or AnimationPlaybackState.Cancelled)
        {
            return false;
        }

        TimePoint now = ReadClock();
        long current = At(now);
        output.BeginEvaluation();
        if (State == AnimationPlaybackState.Paused)
        {
            // Sampling a paused position must not consume crossings captured by Pause.
            Position = Map(current);
            _timeline.Root.Sample(Position.Ticks, output, false, 0);
            return false;
        }

        bool advancing = State == AnimationPlaybackState.Playing && current > _lastEvaluation;
        if (advancing)
        {
            _events.Reset();
            long length = _timeline.Duration.Ticks;
            long first = _wrapMode == AnimationWrapMode.Loop ? Math.Max(0, (_lastEvaluation / length) - 1) : 0;
            long last = _wrapMode == AnimationWrapMode.Loop ? current / length : 0;
            if (!_timeline.Root.HasEvents)
            {
                first = Math.Max(first, last - 1);
            }

            for (long loop = first; (_timeline.Root.HasEvents || _timeline.Root.HasRelease) && loop <= last; loop++)
            {
                if (loop > first)
                {
                    // A new outer loop supersedes contributions from its predecessor.
                    output.BeginEvaluation();
                }

                long origin = checked(loop * length);
                _events.BeginLoop(loop, origin);
                _timeline.Root.Events(new EventQuery(_lastEvaluation - origin, current - origin, _includeStart && _lastEvaluation == 0, true, origin, 1), _events);
                _timeline.Root.Endpoints(_lastEvaluation - origin, current - origin, output, 0);
                if (loop == last)
                {
                    // The final loop index can itself be long.MaxValue.
                    break;
                }
            }

            _events.CopyTo(events, this);
            _includeStart = false;
        }

        Position = Map(current);
        _timeline.Root.Sample(Position.Ticks, output, false, 0);
        _lastEvaluation = current;
        bool completed = State == AnimationPlaybackState.Playing && _wrapMode == AnimationWrapMode.Once && current == _timeline.Duration.Ticks;
        if (completed)
        {
            _anchorPosition = current;
            _anchorClock = now;
            State = AnimationPlaybackState.Completed;
        }

        _lastEvaluationClock = now;
        if (advancing)
        {
            // Preserve the clock anchor used by At, including its fractional progress.
            // Older speed segments are no longer needed once their events were traversed.
            TimingAnchor active = _timing[^1];
            _timing.Clear();
            _timing.Add(active);
        }

        return completed;
    }

    internal Duration OffsetForPosition(long position)
    {
        for (int index = 0; index < _timing.Count; index++)
        {
            TimingAnchor anchor = _timing[index];
            long end = index + 1 < _timing.Count ? _timing[index + 1].Position : long.MaxValue;
            if (anchor.Speed > 0 && position >= anchor.Position && position <= end)
            {
                long distance = position - anchor.Position;
                long elapsed = anchor.Speed == 1 ? distance : checked((long)(distance / anchor.Speed));
                return (anchor.Clock + Duration.FromTicks(elapsed)) - _lastEvaluationClock;
            }
        }

        return Duration.Zero;
    }

    private TimePoint ReadClock()
    {
        TimePoint now = _clock.Now;
        if (now < _lastObserved)
        {
            throw new InvalidOperationException("A monotonic clock moved backwards.");
        }

        _lastObserved = now;
        return now;
    }

    private long At(TimePoint now)
    {
        if (State != AnimationPlaybackState.Playing)
        {
            return _anchorPosition;
        }

        long elapsed = (now - _anchorClock).Ticks;
        long scaled = _speed == 1 ? elapsed : checked((long)(elapsed * _speed));
        long position = checked(_anchorPosition + scaled);
        return _wrapMode == AnimationWrapMode.Once ? Math.Min(position, _timeline.Duration.Ticks) : position;
    }

    private Duration Map(long position) => Duration.FromTicks(_wrapMode == AnimationWrapMode.Loop ? position % _timeline.Duration.Ticks : position);

    private readonly record struct TimingAnchor(long Position, TimePoint Clock, double Speed);
}
