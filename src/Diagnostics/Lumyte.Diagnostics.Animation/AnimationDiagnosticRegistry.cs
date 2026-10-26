using Lumyte.Animation;

namespace Lumyte.Diagnostics.Animation;

/// <summary>Registers animation execution objects for scoped diagnostic access.</summary>
public sealed class AnimationDiagnosticRegistry
{
    internal const int MaximumTargets = 128;
    private readonly Dictionary<string, Target> _targets = new(StringComparer.Ordinal);

    /// <summary>Registers one playback under a stable lowercase ID.</summary>
    /// <param name="id">The target ID.</param>
    /// <param name="playback">The playback.</param>
    /// <returns>A registration which removes only this target when disposed.</returns>
    public AnimationDiagnosticRegistration Register(string id, AnimationPlayback playback)
    {
        ArgumentNullException.ThrowIfNull(playback);
        return Add(id, new PlaybackTarget(id, playback));
    }

    /// <summary>Registers one animation state machine and its explicitly exposed trigger names.</summary>
    /// <typeparam name="TState">The external state identifier type.</typeparam>
    /// <typeparam name="TContext">The application context type.</typeparam>
    /// <param name="id">The target ID.</param>
    /// <param name="machine">The state machine.</param>
    /// <param name="formatState">Formats state identifiers for detached snapshots.</param>
    /// <param name="triggers">Optional diagnostic trigger names and keys.</param>
    /// <returns>A registration which removes only this target when disposed.</returns>
    public AnimationDiagnosticRegistration Register<TState, TContext>(
        string id,
        AnimationStateMachine<TState, TContext> machine,
        Func<TState, string> formatState,
        IReadOnlyDictionary<string, StateMachineTrigger>? triggers = null)
        where TState : notnull
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(formatState);
        var exposed = new Dictionary<string, StateMachineTrigger>(StringComparer.Ordinal);
        if (triggers != null)
        {
            foreach ((string name, StateMachineTrigger trigger) in triggers)
            {
                ValidateName(name, nameof(triggers));
                ArgumentNullException.ThrowIfNull(trigger);
                exposed.Add(name, trigger);
            }
        }

        return Add(id, new MachineTarget<TState, TContext>(id, machine, formatState, exposed));
    }

    internal string List()
        => string.Join('\n', _targets.Keys.Order(StringComparer.Ordinal));

    internal bool TryGet(string id, out Target? target)
        => _targets.TryGetValue(id, out target);

    internal void Remove(string id, object target)
    {
        if (_targets.TryGetValue(id, out Target? current) && ReferenceEquals(current, target))
        {
            _targets.Remove(id);
        }
    }

    private static void ValidateName(string value, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        if (value.Length > 128 || value.Any(character => character is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_' or '.')))
        {
            throw new ArgumentException("Diagnostic names must use 1-128 lowercase ASCII identifier characters.", parameter);
        }
    }

    private AnimationDiagnosticRegistration Add(string id, Target target)
    {
        ValidateName(id, nameof(id));
        if (_targets.Count >= MaximumTargets)
        {
            throw new InvalidOperationException("The animation diagnostic target limit was reached.");
        }

        if (!_targets.TryAdd(id, target))
        {
            throw new ArgumentException("The animation diagnostic target ID is already registered.", nameof(id));
        }

        return new(this, id, target);
    }

    internal abstract class Target(string id)
    {
        protected string Id { get; } = id;

        public abstract AnimationDiagnosticSnapshot Snapshot();

        public abstract bool Play();

        public abstract bool Pause();

        public abstract bool Stop();

        public abstract bool Seek(long ticks);

        public abstract bool SetSpeed(double speed);

        public abstract bool SetTrigger(string trigger);

        public abstract bool ClearTriggers();
    }

    private sealed class PlaybackTarget(string id, AnimationPlayback playback) : Target(id)
    {
        public override AnimationDiagnosticSnapshot Snapshot()
            => new(Id, "playback", playback.State.ToString(), string.Empty, playback.Position.Ticks, playback.Speed);

        public override bool Play()
        {
            playback.Play();
            return true;
        }

        public override bool Pause()
        {
            playback.Pause();
            return true;
        }

        public override bool Stop()
        {
            playback.Stop();
            return true;
        }

        public override bool Seek(long ticks)
        {
            playback.Seek(Lumyte.Core.Time.Duration.FromTicks(ticks));
            return true;
        }

        public override bool SetSpeed(double speed)
        {
            playback.Speed = speed;
            return true;
        }

        public override bool SetTrigger(string trigger) => false;

        public override bool ClearTriggers() => false;
    }

    private sealed class MachineTarget<TState, TContext>(
        string id,
        AnimationStateMachine<TState, TContext> machine,
        Func<TState, string> formatState,
        IReadOnlyDictionary<string, StateMachineTrigger> triggers) : Target(id)
        where TState : notnull
    {
        public override AnimationDiagnosticSnapshot Snapshot()
        {
            bool stopped = machine.Status == AnimationStateMachineStatus.Stopped;
            string state = stopped ? string.Empty : formatState(machine.CurrentState)
                ?? throw new InvalidOperationException("The animation state formatter returned null.");
            if (state.Length > 4096)
            {
                throw new InvalidOperationException("The formatted animation state exceeds the diagnostic limit.");
            }

            return new(Id, "state-machine", machine.Status.ToString(), state, stopped ? 0 : machine.Position.Ticks, machine.Speed);
        }

        public override bool Play()
        {
            if (machine.Status == AnimationStateMachineStatus.Stopped)
            {
                return false;
            }

            machine.Resume();
            return true;
        }

        public override bool Pause()
        {
            if (machine.Status == AnimationStateMachineStatus.Stopped)
            {
                return false;
            }

            machine.Pause();
            return true;
        }

        public override bool Stop()
        {
            machine.Stop();
            return true;
        }

        public override bool Seek(long ticks) => false;

        public override bool SetSpeed(double speed)
        {
            machine.Speed = speed;
            return true;
        }

        public override bool SetTrigger(string trigger)
        {
            if (!triggers.TryGetValue(trigger, out StateMachineTrigger? value))
            {
                return false;
            }

            machine.SetTrigger(value);
            return true;
        }

        public override bool ClearTriggers()
        {
            if (machine.Status == AnimationStateMachineStatus.Stopped)
            {
                return false;
            }

            machine.ClearTriggers();
            return true;
        }
    }
}
