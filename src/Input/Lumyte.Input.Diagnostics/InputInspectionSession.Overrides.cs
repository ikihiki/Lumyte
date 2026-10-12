using System.Numerics;
using Lumyte.Input.Actions;

namespace Lumyte.Input.Diagnostics;

#pragma warning disable SA1204 // Partial implementation groups helpers with the feature they support.

/// <summary>Implements bounded diagnostic input control for the inspection session.</summary>
public sealed partial class InputInspectionSession
{
    private const int MaximumOperations = 64;
    private static readonly TimeSpan _liveTimeout = TimeSpan.FromSeconds(2);
    private readonly Dictionary<InputOverrideTarget, TargetEntry> _targets = [];
    private readonly Dictionary<(ActionSystem System, string Context, string Action), InputOverrideTarget> _targetLookup = [];
    private readonly Dictionary<Guid, LeaseEntry> _leases = [];
    private readonly Dictionary<InputOverrideTarget, Guid> _reservations = [];
    private readonly Dictionary<Guid, PlaybackEntry> _playbacks = [];
    private readonly Dictionary<Guid, LiveEntry> _live = [];
    private long _frame = -1;
    private TimeSpan _logicalNow;

    /// <summary>Gets the opaque target for a context-scoped action in the current profile generation.</summary>
    /// <param name="system">The registered action system.</param>
    /// <param name="contextId">The effective context identifier.</param>
    /// <param name="actionId">The action identifier.</param>
    /// <returns>The generation-bound target.</returns>
    public InputOverrideTarget GetActionTarget(ActionSystem system, string contextId, string actionId)
    {
        ActionView view = Action(system);
        ArgumentException.ThrowIfNullOrWhiteSpace(contextId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        if (!view.Profile.Contexts.Any(context => context.Id == contextId)
            || !EffectiveActions(view.Profile, view.Profile.Contexts.First(context => context.Id == contextId), view.Profile.Contexts.ToDictionary(context => context.Id, StringComparer.Ordinal)).Any(action => action.Id == actionId))
        {
            throw new ArgumentException("Unknown context action.", nameof(actionId));
        }

        (ActionSystem, string, string) key = (system, contextId, actionId);
        if (!_targetLookup.TryGetValue(key, out InputOverrideTarget target))
        {
            throw new InvalidOperationException("The action target was not registered.");
        }

        return target;
    }

    /// <summary>Advances deterministic Tape execution and expires abandoned live sessions.</summary>
    /// <param name="frame">The strictly increasing engine frame.</param>
    public void BeginFrame(long frame)
    {
        Check();
        if (frame < _frame)
        {
            throw new ArgumentOutOfRangeException(nameof(frame), "Frame numbers must increase.");
        }

        if (frame == _frame)
        {
            return;
        }

        _frame = frame;
        foreach (LiveEntry live in _live.Values.Where(entry => _logicalNow - entry.LastHeartbeat >= _liveTimeout).ToArray())
        {
            StopLive(live, "heartbeat-timeout");
        }

        foreach (PlaybackEntry playback in _playbacks.Values.Where(entry => entry.Running && entry.StartFrame <= frame).ToArray())
        {
            AdvanceTape(playback, frame);
        }
    }

    /// <summary>Releases all operations owned by a disconnected diagnostic session.</summary>
    /// <param name="sessionId">The closed diagnostic session.</param>
    public void NotifySessionClosed(Guid sessionId)
    {
        Check();
        foreach (LeaseEntry lease in _leases.Values.Where(entry => entry.Owner.SessionId == sessionId).ToArray())
        {
            ReleaseLease(lease, "session-closed");
        }

        foreach (PlaybackEntry playback in _playbacks.Values.Where(entry => entry.Owner.SessionId == sessionId && entry.Running).ToArray())
        {
            StopTape(playback, "session-closed");
        }

        foreach (LiveEntry live in _live.Values.Where(entry => entry.Owner.SessionId == sessionId).ToArray())
        {
            StopLive(live, "session-closed");
        }
    }

    /// <inheritdoc/>
    public Vector2 EvaluateValue(ActionSystem system, string contextId, string actionId, ActionValueKind kind, Vector2 value, TimeSpan now)
    {
        _ = Action(system);
        if (now < _logicalNow)
        {
            throw new ArgumentOutOfRangeException(nameof(now));
        }

        _logicalNow = now;
        if (!_targetLookup.TryGetValue((system, contextId, actionId), out InputOverrideTarget target))
        {
            target = GetActionTarget(system, contextId, actionId);
        }

        if (!_reservations.TryGetValue(target, out Guid reservation))
        {
            return value;
        }

        if (_leases.TryGetValue(reservation, out LeaseEntry? lease))
        {
            if (now >= lease.ExpiresAt)
            {
                ReleaseLease(lease, "expired");
                return value;
            }

            return Combine(value, lease.Spec);
        }

        if (_playbacks.TryGetValue(reservation, out PlaybackEntry? playback) && playback.Values.TryGetValue(target, out Vector2 tapeValue))
        {
            return tapeValue;
        }

        if (_live.TryGetValue(reservation, out LiveEntry? live) && live.Values.TryGetValue(target, out Vector2 liveValue))
        {
            return liveValue;
        }

        return value;
    }

    /// <inheritdoc/>
    public InputOverrideResult Acquire(InputOverrideOwner owner, InputOverrideTarget target, InputOverrideSpec spec)
    {
        CheckOwner(owner);
        if (!ValidateSpec(spec, out string? error))
        {
            return Reject("invalid-spec", error!);
        }

        if (!_targets.ContainsKey(target))
        {
            return Reject("target-stale", "The target does not belong to the current profile generation.");
        }

        if (_reservations.ContainsKey(target))
        {
            return Reject("target-busy", "The target is already controlled.");
        }

        if (_leases.Count + _playbacks.Count(entry => entry.Value.Running) + _live.Count >= MaximumOperations)
        {
            return Reject("operation-capacity", "The diagnostic input operation capacity is full.");
        }

        var lease = new LeaseEntry(Guid.NewGuid(), owner, target, spec, 1, _logicalNow + spec.Duration);
        _leases.Add(lease.Id, lease);
        _reservations.Add(target, lease.Id);
        _revision++;
        return Success(lease);
    }

    /// <inheritdoc/>
    public InputOverrideResult Update(InputOverrideOwner owner, Guid leaseId, long expectedRevision, InputOverrideSpec spec)
    {
        CheckOwner(owner);
        if (!_leases.TryGetValue(leaseId, out LeaseEntry? lease) || lease.Owner != owner)
        {
            return Reject("lease-not-found", "The lease is not owned by this actor.");
        }

        if (lease.Revision != expectedRevision)
        {
            return Reject("revision-conflict", "The lease revision has changed.");
        }

        if (!ValidateSpec(spec, out string? error))
        {
            return Reject("invalid-spec", error!);
        }

        lease.Spec = spec;
        lease.Revision++;
        lease.ExpiresAt = _logicalNow + spec.Duration;
        _revision++;
        return Success(lease);
    }

    /// <inheritdoc/>
    public InputOverrideResult Release(InputOverrideOwner owner, Guid leaseId, long expectedRevision)
    {
        CheckOwner(owner);
        if (!_leases.TryGetValue(leaseId, out LeaseEntry? lease) || lease.Owner != owner)
        {
            return Reject("lease-not-found", "The lease is not owned by this actor.");
        }

        if (lease.Revision != expectedRevision)
        {
            return Reject("revision-conflict", "The lease revision has changed.");
        }

        InputOverrideLease result = Snapshot(lease, active: false);
        ReleaseLease(lease, "released");
        return new(true, result);
    }

    /// <inheritdoc/>
    public InputOverrideResult GetLease(InputOverrideOwner owner, Guid leaseId)
    {
        CheckOwner(owner);
        return _leases.TryGetValue(leaseId, out LeaseEntry? lease) && lease.Owner == owner
            ? Success(lease)
            : Reject("lease-not-found", "The lease is not owned by this actor.");
    }

    /// <inheritdoc/>
    public InputTapePlayback Play(InputOverrideOwner owner, InputTape tape, IReadOnlyList<InputOverrideTarget> tracks, long startFrame, int repeatCount = 1)
    {
        CheckOwner(owner);
        ArgumentNullException.ThrowIfNull(tape);
        ArgumentNullException.ThrowIfNull(tracks);
        ValidateTape(tape, tracks, startFrame, repeatCount);
        var id = Guid.NewGuid();
        ReserveAll(id, tracks);
        var playback = new PlaybackEntry(id, owner, tape, tracks.ToArray(), startFrame, repeatCount);
        _playbacks.Add(id, playback);
        _revision++;
        return TapeSnapshot(playback);
    }

    /// <inheritdoc/>
    public InputTapePlayback GetPlayback(InputOverrideOwner owner, Guid playbackId)
    {
        CheckOwner(owner);
        PlaybackEntry playback = OwnedPlayback(owner, playbackId);
        return TapeSnapshot(playback);
    }

    /// <inheritdoc/>
    public InputTapePlayback Stop(InputOverrideOwner owner, Guid playbackId, long expectedRevision)
    {
        CheckOwner(owner);
        PlaybackEntry playback = OwnedPlayback(owner, playbackId);
        if (playback.Revision != expectedRevision)
        {
            throw new InvalidOperationException("revision-conflict");
        }

        StopTape(playback, "stopped");
        return TapeSnapshot(playback);
    }

    /// <inheritdoc/>
    public InputLiveSession Begin(InputOverrideOwner owner, IReadOnlyList<InputOverrideTarget> targets)
    {
        CheckOwner(owner);
        ArgumentNullException.ThrowIfNull(targets);
        var id = Guid.NewGuid();
        ReserveAll(id, targets);
        var live = new LiveEntry(id, owner, targets.ToArray(), _logicalNow);
        _live.Add(id, live);
        _revision++;
        return new(id, live.Revision);
    }

    /// <inheritdoc/>
    public void Send(InputOverrideOwner owner, Guid sessionId, long batchSequence, IReadOnlyList<InputLiveValue> orderedValues)
    {
        CheckOwner(owner);
        LiveEntry live = OwnedLive(owner, sessionId);
        ArgumentNullException.ThrowIfNull(orderedValues);
        if (batchSequence <= live.BatchSequence)
        {
            throw new InvalidOperationException("batch-sequence-conflict");
        }

        var next = new Dictionary<InputOverrideTarget, Vector2>();
        foreach (InputLiveValue item in orderedValues)
        {
            if (!live.Targets.Contains(item.Target) || !float.IsFinite(item.Value.X) || !float.IsFinite(item.Value.Y) || !next.TryAdd(item.Target, Vector2.Clamp(item.Value, -Vector2.One, Vector2.One)))
            {
                throw new ArgumentException("Invalid or duplicate live value.", nameof(orderedValues));
            }
        }

        live.Values.Clear();
        foreach ((InputOverrideTarget target, Vector2 value) in next)
        {
            live.Values.Add(target, value);
        }

        live.BatchSequence = batchSequence;
        live.LastHeartbeat = _logicalNow;
        live.Revision++;
        _revision++;
    }

    /// <inheritdoc/>
    public void Heartbeat(InputOverrideOwner owner, Guid sessionId)
    {
        CheckOwner(owner);
        LiveEntry live = OwnedLive(owner, sessionId);
        live.LastHeartbeat = _logicalNow;
        live.Revision++;
    }

    /// <inheritdoc/>
    public void Stop(InputOverrideOwner owner, Guid sessionId)
    {
        CheckOwner(owner);
        StopLive(OwnedLive(owner, sessionId), "stopped");
    }

    private static Vector2 Combine(Vector2 value, InputOverrideSpec spec) => spec.Mode == InputOverrideMode.Replace ? spec.Value : Vector2.Clamp(value + spec.Value, -Vector2.One, Vector2.One);

    private static bool ValidateSpec(InputOverrideSpec spec, out string? error)
    {
        error = null;
        if (!Enum.IsDefined(spec.Mode) || !float.IsFinite(spec.Value.X) || !float.IsFinite(spec.Value.Y) || spec.Duration <= TimeSpan.Zero || spec.Duration > TimeSpan.FromSeconds(5))
        {
            error = "Mode, value, and duration must be finite and duration must be at most five seconds.";
            return false;
        }

        return true;
    }

    private void ValidateTape(InputTape tape, IReadOnlyList<InputOverrideTarget> tracks, long startFrame, int repeatCount)
    {
        long currentFrame = Math.Max(0, _frame);
        if (tape.Version != 1 || tape.Tracks.IsDefaultOrEmpty || tape.Commands.IsDefaultOrEmpty || tracks.Count != tape.Tracks.Length || startFrame < currentFrame || startFrame > currentFrame + 120 || repeatCount is < 1 or > 1000 || tape.Commands.Length > 10000
            || tracks.Distinct().Count() != tracks.Count || tracks.Any(target => !_targets.ContainsKey(target))
            || tape.Commands.Any(command => !Enum.IsDefined(command.Kind) || command.Track < 0 || command.Track >= tracks.Count || command.Frames is < 0 or > 1000000 || !float.IsFinite(command.Value.X) || !float.IsFinite(command.Value.Y)))
        {
            throw new ArgumentException("Invalid Tape.", nameof(tape));
        }

        bool changed = false;
        foreach (InputTapeCommand command in tape.Commands)
        {
            if (command.Kind == InputTapeCommandKind.WaitFrames)
            {
                if (command.Frames == 0 && changed)
                {
                    throw new ArgumentException("A zero-frame wait cannot separate Action transitions.", nameof(tape));
                }

                changed = false;
            }
            else if (changed)
            {
                throw new ArgumentException("Tape permits one Action transition per frame boundary.", nameof(tape));
            }
            else
            {
                changed = true;
            }
        }
    }

    private void AdvanceTape(PlaybackEntry playback, long frame)
    {
        if (playback.WaitUntil > frame)
        {
            return;
        }

        while (playback.Running)
        {
            if (playback.CommandIndex == playback.Tape.Commands.Length)
            {
                playback.RepeatsRemaining--;
                if (playback.RepeatsRemaining == 0)
                {
                    StopTape(playback, "completed");
                    return;
                }

                playback.CommandIndex = 0;
            }

            InputTapeCommand command = playback.Tape.Commands[(int)playback.CommandIndex++];
            playback.Revision++;
            if (command.Kind == InputTapeCommandKind.WaitFrames)
            {
                playback.WaitUntil = checked(frame + command.Frames);
                if (command.Frames > 0)
                {
                    return;
                }

                continue;
            }

            Vector2 value = command.Kind switch
            {
                InputTapeCommandKind.Press => Vector2.UnitX,
                InputTapeCommandKind.Release => Vector2.Zero,
                _ => Vector2.Clamp(command.Value, -Vector2.One, Vector2.One),
            };
            playback.Values[playback.Targets[command.Track]] = value;
        }
    }

    private void ReserveAll(Guid id, IReadOnlyList<InputOverrideTarget> targets)
    {
        if (_leases.Count + _playbacks.Count(entry => entry.Value.Running) + _live.Count >= MaximumOperations)
        {
            throw new InvalidOperationException("operation-capacity");
        }

        if (targets.Count is < 1 or > 256 || targets.Distinct().Count() != targets.Count || targets.Any(target => !_targets.ContainsKey(target)))
        {
            throw new ArgumentException("Targets must be current, unique, and bounded.", nameof(targets));
        }

        if (targets.Any(_reservations.ContainsKey))
        {
            throw new InvalidOperationException("target-busy");
        }

        foreach (InputOverrideTarget target in targets)
        {
            _reservations.Add(target, id);
        }
    }

    private void InvalidateTargets(ActionSystem system, string reason)
    {
        foreach (InputOverrideTarget target in _targets.Where(pair => ReferenceEquals(pair.Value.System, system)).Select(pair => pair.Key).ToArray())
        {
            if (_reservations.TryGetValue(target, out Guid reservation))
            {
                if (_leases.TryGetValue(reservation, out LeaseEntry? lease))
                {
                    ReleaseLease(lease, reason);
                }

                if (_playbacks.TryGetValue(reservation, out PlaybackEntry? playback))
                {
                    StopTape(playback, reason);
                }

                if (_live.TryGetValue(reservation, out LiveEntry? live))
                {
                    StopLive(live, reason);
                }
            }

            _targets.Remove(target);
        }

        foreach ((ActionSystem System, string Context, string Action) key in _targetLookup.Keys.Where(key => ReferenceEquals(key.System, system)).ToArray())
        {
            _targetLookup.Remove(key);
        }
    }

    private void RegisterTargets(ActionSystem system, ActionProfile profile)
    {
        var contexts = profile.Contexts.ToDictionary(context => context.Id, StringComparer.Ordinal);
        foreach (InputContext context in profile.Contexts)
        {
            foreach (ActionDefinition action in EffectiveActions(profile, context, contexts))
            {
                var target = new InputOverrideTarget(Guid.NewGuid());
                _targetLookup.Add((system, context.Id, action.Id), target);
                _targets.Add(target, new(system, context.Id, action.Id));
            }
        }
    }

    private void ReleaseLease(LeaseEntry lease, string reason)
    {
        _reservations.Remove(lease.Target);
        _leases.Remove(lease.Id);
        _revision++;
    }

    private void StopTape(PlaybackEntry playback, string reason)
    {
        if (!playback.Running)
        {
            return;
        }

        playback.Running = false;
        playback.StopReason = reason;
        playback.Revision++;
        foreach (InputOverrideTarget target in playback.Targets)
        {
            _reservations.Remove(target);
        }

        playback.Values.Clear();
        _revision++;
    }

    private void StopLive(LiveEntry live, string reason)
    {
        foreach (InputOverrideTarget target in live.Targets)
        {
            _reservations.Remove(target);
        }

        _live.Remove(live.Id);
        _revision++;
    }

    private static InputOverrideResult Reject(string code, string message) => new(false, null, code, message);

    private InputOverrideResult Success(LeaseEntry lease) => new(true, Snapshot(lease, true));

    private InputOverrideLease Snapshot(LeaseEntry lease, bool active) => new(lease.Id, lease.Target, lease.Spec, lease.Revision, active ? TimeSpan.FromTicks(Math.Max(0, (lease.ExpiresAt - _logicalNow).Ticks)) : TimeSpan.Zero, active);

    private static InputTapePlayback TapeSnapshot(PlaybackEntry playback) => new(playback.Id, playback.Revision, playback.CommandIndex, playback.Running, playback.StopReason);

    private PlaybackEntry OwnedPlayback(InputOverrideOwner owner, Guid id) => _playbacks.TryGetValue(id, out PlaybackEntry? value) && value.Owner == owner ? value : throw new KeyNotFoundException("playback-not-found");

    private LiveEntry OwnedLive(InputOverrideOwner owner, Guid id) => _live.TryGetValue(id, out LiveEntry? value) && value.Owner == owner ? value : throw new KeyNotFoundException("live-session-not-found");

    private static void CheckOwner(InputOverrideOwner owner)
    {
        if (owner.SessionId == Guid.Empty || string.IsNullOrWhiteSpace(owner.ActorId))
        {
            throw new ArgumentException("An authenticated owner is required.", nameof(owner));
        }
    }

    private sealed record TargetEntry(ActionSystem System, string ContextId, string ActionId);

    private sealed class LeaseEntry(Guid id, InputOverrideOwner owner, InputOverrideTarget target, InputOverrideSpec spec, long revision, TimeSpan expiresAt)
    {
        public Guid Id { get; } = id;

        public InputOverrideOwner Owner { get; } = owner;

        public InputOverrideTarget Target { get; } = target;

        public InputOverrideSpec Spec { get; set; } = spec;

        public long Revision { get; set; } = revision;

        public TimeSpan ExpiresAt { get; set; } = expiresAt;
    }

    private sealed class PlaybackEntry(Guid id, InputOverrideOwner owner, InputTape tape, InputOverrideTarget[] targets, long startFrame, int repeats)
    {
        public Guid Id { get; } = id;

        public InputOverrideOwner Owner { get; } = owner;

        public InputTape Tape { get; } = tape;

        public InputOverrideTarget[] Targets { get; } = targets;

        public long StartFrame { get; } = startFrame;

        public int RepeatsRemaining { get; set; } = repeats;

        public long CommandIndex { get; set; }

        public long WaitUntil { get; set; } = startFrame;

        public long Revision { get; set; } = 1;

        public bool Running { get; set; } = true;

        public string? StopReason { get; set; }

        public Dictionary<InputOverrideTarget, Vector2> Values { get; } = [];
    }

    private sealed class LiveEntry(Guid id, InputOverrideOwner owner, InputOverrideTarget[] targets, TimeSpan now)
    {
        public Guid Id { get; } = id;

        public InputOverrideOwner Owner { get; } = owner;

        public InputOverrideTarget[] Targets { get; } = targets;

        public Dictionary<InputOverrideTarget, Vector2> Values { get; } = [];

        public long Revision { get; set; } = 1;

        public long BatchSequence { get; set; } = -1;

        public TimeSpan LastHeartbeat { get; set; } = now;
    }
}

#pragma warning restore SA1204
