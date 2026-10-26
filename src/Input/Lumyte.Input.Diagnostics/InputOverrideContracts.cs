using System.Collections.Immutable;
using System.Numerics;
using Lumyte.Input.Actions;

namespace Lumyte.Input.Diagnostics;

#pragma warning disable SA1201, SA1402, SA1611, SA1616, SA1649 // Closely related wire contracts are kept together.

/// <summary>Defines how an injected value combines with the corrected action value.</summary>
public enum InputOverrideMode
{
    /// <summary>Replaces the corrected value.</summary>
    Replace,

    /// <summary>Adds to the corrected value before action-kind normalization.</summary>
    Add,
}

/// <summary>Identifies the authenticated diagnostic owner of an operation.</summary>
/// <param name="SessionId">The diagnostic connection.</param>
/// <param name="ActorId">The authenticated actor.</param>
public readonly record struct InputOverrideOwner(Guid SessionId, string ActorId);

/// <summary>Opaque handle bound to one action-profile generation or device connection.</summary>
/// <param name="Id">The opaque identifier.</param>
public readonly record struct InputOverrideTarget(Guid Id);

/// <summary>Describes a bounded action override.</summary>
/// <param name="Mode">The combination mode.</param>
/// <param name="Value">The injected value.</param>
/// <param name="Duration">The logical duration.</param>
public sealed record InputOverrideSpec(InputOverrideMode Mode, Vector2 Value, TimeSpan Duration);

/// <summary>Reports the current lease state.</summary>
public sealed record InputOverrideLease(Guid Id, InputOverrideTarget Target, InputOverrideSpec Spec, long Revision, TimeSpan Remaining, bool IsActive);

/// <summary>Returns either a lease or a stable rejection code.</summary>
public sealed record InputOverrideResult(bool IsSuccess, InputOverrideLease? Lease, string? ErrorCode = null, string? ErrorMessage = null);

/// <summary>Defines one Tape command.</summary>
public enum InputTapeCommandKind
{
    /// <summary>Sets a button-like value to one.</summary>
    Press,

    /// <summary>Sets a value to zero.</summary>
    Release,

    /// <summary>Sets the supplied value.</summary>
    Set,

    /// <summary>Waits the supplied number of frame boundaries.</summary>
    WaitFrames,
}

/// <summary>One ordered Tape instruction.</summary>
public sealed record InputTapeCommand(InputTapeCommandKind Kind, int Track, Vector2 Value, long Frames = 0);

/// <summary>One typed Tape track.</summary>
public sealed record InputTapeTrack(string Alias, ActionValueKind Kind);

/// <summary>Immutable frame-based input program.</summary>
public sealed record InputTape(int Version, ImmutableArray<InputTapeTrack> Tracks, ImmutableArray<InputTapeCommand> Commands);

/// <summary>Current Tape playback cursor.</summary>
public sealed record InputTapePlayback(Guid Id, long Revision, long CommandIndex, bool IsRunning, string? StopReason);

/// <summary>Live input session identity.</summary>
public sealed record InputLiveSession(Guid Id, long Revision);

/// <summary>One value in an ordered live batch.</summary>
public sealed record InputLiveValue(InputOverrideTarget Target, Vector2 Value);

/// <summary>Controls direct value leases.</summary>
public interface IInputOverrideService
{
    /// <summary>Acquires exclusive control of a target.</summary>
    /// <returns></returns>
    InputOverrideResult Acquire(InputOverrideOwner owner, InputOverrideTarget target, InputOverrideSpec spec);

    /// <summary>Updates an owned lease using optimistic concurrency.</summary>
    /// <returns></returns>
    InputOverrideResult Update(InputOverrideOwner owner, Guid leaseId, long expectedRevision, InputOverrideSpec spec);

    /// <summary>Releases an owned lease using optimistic concurrency.</summary>
    /// <returns></returns>
    InputOverrideResult Release(InputOverrideOwner owner, Guid leaseId, long expectedRevision);

    /// <summary>Reads an owned lease.</summary>
    /// <returns></returns>
    InputOverrideResult GetLease(InputOverrideOwner owner, Guid leaseId);
}

/// <summary>Controls deterministic frame-based Tape playback.</summary>
public interface IInputTapeService
{
    /// <summary>Validates, reserves, and starts a Tape atomically.</summary>
    /// <returns></returns>
    InputTapePlayback Play(InputOverrideOwner owner, InputTape tape, IReadOnlyList<InputOverrideTarget> tracks, long startFrame, int repeatCount = 1);

    /// <summary>Reads an owned playback.</summary>
    /// <returns></returns>
    InputTapePlayback GetPlayback(InputOverrideOwner owner, Guid playbackId);

    /// <summary>Stops an owned playback.</summary>
    /// <returns></returns>
    InputTapePlayback Stop(InputOverrideOwner owner, Guid playbackId, long expectedRevision);
}

/// <summary>Controls ordered live input batches.</summary>
public interface IInputLiveService
{
    /// <summary>Atomically reserves targets for one live producer.</summary>
    /// <returns></returns>
    InputLiveSession Begin(InputOverrideOwner owner, IReadOnlyList<InputOverrideTarget> targets);

    /// <summary>Applies one strictly ordered batch.</summary>
    void Send(InputOverrideOwner owner, Guid sessionId, long batchSequence, IReadOnlyList<InputLiveValue> orderedValues);

    /// <summary>Renews the live producer timeout.</summary>
    void Heartbeat(InputOverrideOwner owner, Guid sessionId);

    /// <summary>Stops the producer and releases all targets.</summary>
    void Stop(InputOverrideOwner owner, Guid sessionId);
}

#pragma warning restore SA1201, SA1402, SA1611, SA1616, SA1649
