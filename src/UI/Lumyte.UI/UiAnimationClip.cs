using System.Collections.Concurrent;
using Lumyte.Animation;
using Lumyte.Core.Time;
using Lumyte.Signals;

namespace Lumyte.UI;

/// <summary>A passive timeline definition with queued controls and surface-owned playback.</summary>
public sealed class UiAnimationClip
{
    private readonly ClipProducer _producer;
    private readonly Dictionary<object, object> _channels = [];

    internal UiAnimationClip(AnimationTimeline timeline, AnimationWrapMode wrapMode, Action<AnimationEventOccurrence>? onEvent, Action? completed)
    {
        _producer = new(timeline, wrapMode, onEvent, completed);
    }

    private enum Control
    {
        Play,
        Pause,
        Resume,
        Seek,
        Stop,
        Cancel,
        Speed,
    }

    /// <summary>Gets the last applied playback state.</summary>
    public AnimationPlaybackState State => _producer.State;

    /// <summary>Gets the last applied position, independently of queued controls.</summary>
    public Duration Position => _producer.Position;

    /// <summary>Gets or sets the finite nonnegative requested speed; zero releases continuous frame requests.</summary>
    public double Speed
    {
        get => _producer.RequestedSpeed;
        set
        {
            if (!double.IsFinite(value) || value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _producer.RequestedSpeed = value;
            _producer.Enqueue(new(Control.Speed, Speed: value));
        }
    }

    /// <summary>Defines a typed output without starting playback or subscribing.</summary>
    /// <typeparam name="T">The exact property type.</typeparam>
    /// <param name="channel">The original timeline channel identity.</param>
    /// <param name="initialValue">The value held until a channel contributes.</param>
    /// <returns>The passive projection, shared for repeated identical channel declarations.</returns>
    public UiAnimated<T> Value<T>(AnimationChannel<T> channel, T initialValue)
    {
        ArgumentNullException.ThrowIfNull(channel);
        if (_channels.TryGetValue(channel, out object? entry))
        {
            if (entry is not Channel<T> existing || !EqualityComparer<T>.Default.Equals(existing.Initial, initialValue))
            {
                throw new ArgumentException("Repeated channels must retain their exact type and initial value.", nameof(initialValue));
            }

            return existing.Value;
        }

        if (_producer.IsConnected)
        {
            throw new InvalidOperationException("Add clip channels before connecting their projections.");
        }

        T held = initialValue;
        var value = new UiAnimated<T>(_producer, () => _producer.Output.TryGet(channel, out T? result) ? result : held, () => held, next => held = next);
        _channels.Add(channel, new Channel<T>(initialValue, value));
        return value;
    }

    /// <summary>Queues playback or restart according to the existing Playback semantics.</summary>
    public void Play() => _producer.Enqueue(new(Control.Play));

    /// <summary>Queues a pause at the next animation phase.</summary>
    public void Pause() => _producer.Enqueue(new(Control.Pause));

    /// <summary>Queues resumption from the applied paused position.</summary>
    public void Resume() => _producer.Enqueue(new(Control.Resume));

    /// <summary>Queues a marker-free sample at the supplied position.</summary>
    /// <param name="position">A position inside the immutable timeline.</param>
    public void Seek(Duration position)
    {
        if (position.Ticks < 0 || position > _producer.Duration)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        _producer.Enqueue(new(Control.Seek, position));
    }

    /// <summary>Queues stopping without restoring display values.</summary>
    public void Stop() => _producer.Enqueue(new(Control.Stop));

    /// <summary>Revokes pending notifications and queues cancellation without restoring values.</summary>
    public void Cancel() => _producer.Cancel();

    private readonly record struct Command(Control Kind, Duration Position = default, double Speed = 1);

    private sealed record Channel<T>(T Initial, UiAnimated<T> Value);

    private sealed class ClipProducer(AnimationTimeline timeline, AnimationWrapMode wrapMode, Action<AnimationEventOccurrence>? onEvent, Action? completed) : UiAnimationProducer
    {
        private readonly ConcurrentQueue<Command> _commands = new();
        private readonly List<AnimationEventOccurrence> _events = [];
        private readonly Signal<ulong> _controlsVersion = Signal.Create(0UL);
        private readonly Clock _clock = new();
        private SignalEffect? _controlWatch;
        private AnimationPlayback? _playback;
        private AnimationPlaybackState _savedState;
        private Duration _savedPosition;
        private bool _initial = true;
        private bool _restore;

        internal AnimationOutput Output { get; } = new();

        internal Duration Duration => timeline.Duration;

        internal AnimationPlaybackState State => _playback?.State ?? _savedState;

        internal Duration Position => _playback?.Position ?? _savedPosition;

        internal double RequestedSpeed { get; set; } = 1;

        internal override ulong Revision => _controlWatch?.Revision ?? 0;

        internal override bool HasPending => _initial || !_commands.IsEmpty;

        internal override bool IsRunning => _playback is { State: AnimationPlaybackState.Playing, Speed: > 0 } && Duration.Ticks > 0;

        internal void Enqueue(Command command)
        {
            _controlsVersion.Value = checked(_controlsVersion.Peek() + 1);
            _commands.Enqueue(command);
        }

        internal void Cancel()
        {
            RevokeNotifications();
            Enqueue(new(Control.Cancel));
        }

        protected override void ConnectCore()
        {
            Scope.Run(() => _controlWatch = Signal.Effect(() => _ = _controlsVersion.Value, this));
            _clock.Value = ActiveTime;
            _playback = new(_clock, timeline, wrapMode);
            _initial = true;
        }

        protected override void EvaluateCore()
        {
            _clock.Value = ActiveTime;
            Output.Clear();
            _events.Clear();
            if (_initial)
            {
                _playback!.Play();
                _playback.Speed = RequestedSpeed;
                if (_restore)
                {
                    _playback.Seek(_savedPosition);
                    switch (_savedState)
                    {
                        case AnimationPlaybackState.Paused:
                            _playback.Pause();
                            break;
                        case AnimationPlaybackState.Stopped:
                            _playback.Stop();
                            break;
                        case AnimationPlaybackState.Cancelled:
                            _playback.Cancel();
                            break;
                    }
                }

                _initial = false;
            }

            int controls = _commands.Count;
            for (int index = 0; index < controls && _commands.TryDequeue(out Command control); index++)
            {
                Apply(control);
            }

            bool finished = _playback!.Update(Output, _events);
            foreach (AnimationEventOccurrence occurrence in _events)
            {
                Notify(onEvent is null ? null : () => onEvent(occurrence));
            }

            if (finished && !(_restore && _savedState == AnimationPlaybackState.Completed))
            {
                Notify(completed);
            }

            _restore = false;
        }

        protected override void CommitCore()
        {
            // Channel last-values are sampled after their staged output has been published.
        }

        protected override void DisconnectCore()
        {
            if (_playback is not null)
            {
                _savedState = _playback.State;
                _savedPosition = _playback.Position;
                _restore = true;
            }

            _playback = null;
            _controlWatch = null;
            Output.Clear();
        }

        private void Apply(Command command)
        {
            switch (command.Kind)
            {
                case Control.Play:
                    _playback!.Play();
                    break;
                case Control.Pause:
                    _playback!.Pause();
                    break;
                case Control.Resume:
                    _playback!.Resume();
                    break;
                case Control.Seek:
                    _playback!.Seek(command.Position);
                    break;
                case Control.Stop:
                    _playback!.Stop();
                    break;
                case Control.Cancel:
                    _playback!.Cancel();
                    break;
                case Control.Speed:
                    _playback!.Speed = command.Speed;
                    break;
            }
        }

        private sealed class Clock : IMonotonicClock
        {
            public TimePoint Now => Value;

            internal TimePoint Value { get; set; }
        }
    }
}
