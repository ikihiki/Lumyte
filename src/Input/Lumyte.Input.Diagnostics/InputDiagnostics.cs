using System.Collections.Immutable;
using System.Text.Json;
using Lumyte.Diagnostics;
using Lumyte.Input.Actions;

namespace Lumyte.Input.Diagnostics;

#pragma warning disable SA1204 // Operation methods are followed by their wire parsing helpers.

/// <summary>Publishes the scoped Input inspection snapshot.</summary>
/// <param name="inspection">The shared Input and Action observer.</param>
public sealed partial class InputDiagnostics(InputInspectionSession inspection)
{
    private readonly InputInspectionSession _inspection = inspection;

    [DiagnosticOperation(DiagnosticPermission.Observe, Id = "get-snapshot", DisplayName = "Input snapshot", Traffic = DiagnosticOperationTraffic.Stream)]
    private DiagnosticResult<InputSnapshotReceipt> GetSnapshot()
    {
        string json = _inspection.CreateSnapshot();
        return json.Length <= 1048576
            ? DiagnosticResult<InputSnapshotReceipt>.Success(new(json), _inspection.Revision)
            : DiagnosticResult<InputSnapshotReceipt>.Reject("snapshot-too-large", "The Input snapshot exceeds one mebibyte.");
    }

    [DiagnosticOperation(DiagnosticPermission.OverrideInput, Id = "override-acquire", DisplayName = "Acquire action override", Traffic = DiagnosticOperationTraffic.Control)]
    private DiagnosticResult<InputOverrideReceipt> Acquire(
        DiagnosticOperationContext context,
        [DiagnosticArgument(MaxLength = 36)] string targetId,
        [DiagnosticArgument(MaxLength = 8)] string mode,
        [DiagnosticArgument(Minimum = -1, Maximum = 1)] double x,
        [DiagnosticArgument(Minimum = -1, Maximum = 1)] double y,
        [DiagnosticArgument(Minimum = 1, Maximum = 5000)] long durationMs)
    {
        _inspection.BeginFrame(context.Frame.Number);
        if (!Guid.TryParse(targetId, out Guid target) || !Enum.TryParse(mode, true, out InputOverrideMode parsedMode))
        {
            return DiagnosticResult<InputOverrideReceipt>.Reject("invalid-argument", "Target or mode is invalid.");
        }

        InputOverrideResult result = _inspection.Acquire(Owner(context), new(target), new(parsedMode, new((float)x, (float)y), TimeSpan.FromMilliseconds(durationMs)));
        return OverrideResult(result);
    }

    [DiagnosticOperation(DiagnosticPermission.OverrideInput, Id = "override-release", DisplayName = "Release action override", RequiresRevision = true, Traffic = DiagnosticOperationTraffic.Control)]
    private DiagnosticResult<InputOverrideReceipt> Release(DiagnosticOperationContext context, [DiagnosticArgument(MaxLength = 36)] string leaseId)
    {
        _inspection.BeginFrame(context.Frame.Number);
        return Guid.TryParse(leaseId, out Guid lease)
            ? OverrideResult(_inspection.Release(Owner(context), lease, context.ExpectedRevision!.Value))
            : DiagnosticResult<InputOverrideReceipt>.Reject("invalid-argument", "Lease ID is invalid.");
    }

    [DiagnosticOperation(DiagnosticPermission.OverrideInput, Id = "override-get", DisplayName = "Read action override", Traffic = DiagnosticOperationTraffic.Control)]
    private DiagnosticResult<InputOverrideReceipt> GetOverride(DiagnosticOperationContext context, [DiagnosticArgument(MaxLength = 36)] string leaseId)
        => Guid.TryParse(leaseId, out Guid lease)
            ? OverrideResult(_inspection.GetLease(Owner(context), lease))
            : DiagnosticResult<InputOverrideReceipt>.Reject("invalid-argument", "Lease ID is invalid.");

    [DiagnosticOperation(DiagnosticPermission.OverrideInput, Id = "override-update", DisplayName = "Update action override", RequiresRevision = true, Traffic = DiagnosticOperationTraffic.Control)]
    private DiagnosticResult<InputOverrideReceipt> UpdateOverride(
        DiagnosticOperationContext context,
        [DiagnosticArgument(MaxLength = 36)] string leaseId,
        [DiagnosticArgument(MaxLength = 8)] string mode,
        [DiagnosticArgument(Minimum = -1, Maximum = 1)] double x,
        [DiagnosticArgument(Minimum = -1, Maximum = 1)] double y,
        [DiagnosticArgument(Minimum = 1, Maximum = 5000)] long durationMs)
    {
        _inspection.BeginFrame(context.Frame.Number);
        if (!Guid.TryParse(leaseId, out Guid lease) || !Enum.TryParse(mode, true, out InputOverrideMode parsedMode))
        {
            return DiagnosticResult<InputOverrideReceipt>.Reject("invalid-argument", "Lease or mode is invalid.");
        }

        return OverrideResult(_inspection.Update(Owner(context), lease, context.ExpectedRevision!.Value, new(parsedMode, new((float)x, (float)y), TimeSpan.FromMilliseconds(durationMs))));
    }

    [DiagnosticOperation(DiagnosticPermission.OverrideInput, Id = "tape-play", DisplayName = "Play Input Tape", Traffic = DiagnosticOperationTraffic.Control)]
    private DiagnosticResult<InputTapeReceipt> PlayTape(
        DiagnosticOperationContext context,
        [DiagnosticArgument(MaxLength = 32768)] string tapeJson,
        [DiagnosticArgument(MaxLength = 16384)] string targetIdsJson,
        [DiagnosticArgument(Minimum = 0)] long startFrame,
        [DiagnosticArgument(Minimum = 1, Maximum = 1000)] long repeatCount)
    {
        try
        {
            _inspection.BeginFrame(context.Frame.Number);
            InputTape tape = ParseTape(tapeJson);
            InputOverrideTarget[] targets = ParseTargets(targetIdsJson);
            InputTapePlayback playback = _inspection.Play(Owner(context), tape, targets, startFrame, checked((int)repeatCount));
            return DiagnosticResult<InputTapeReceipt>.Success(TapeReceipt(playback), playback.Revision);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException or OverflowException)
        {
            return DiagnosticResult<InputTapeReceipt>.Reject("tape-rejected", exception.Message);
        }
    }

    [DiagnosticOperation(DiagnosticPermission.OverrideInput, Id = "tape-stop", DisplayName = "Stop Input Tape", RequiresRevision = true, Traffic = DiagnosticOperationTraffic.Control)]
    private DiagnosticResult<InputTapeReceipt> StopTape(DiagnosticOperationContext context, [DiagnosticArgument(MaxLength = 36)] string playbackId)
    {
        try
        {
            _inspection.BeginFrame(context.Frame.Number);
            InputTapePlayback playback = _inspection.Stop(Owner(context), Guid.Parse(playbackId), context.ExpectedRevision!.Value);
            return DiagnosticResult<InputTapeReceipt>.Success(TapeReceipt(playback), playback.Revision);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            return DiagnosticResult<InputTapeReceipt>.Reject("tape-rejected", exception.Message);
        }
    }

    [DiagnosticOperation(DiagnosticPermission.OverrideInput, Id = "tape-get", DisplayName = "Read Input Tape", Traffic = DiagnosticOperationTraffic.Control)]
    private DiagnosticResult<InputTapeReceipt> GetTape(DiagnosticOperationContext context, [DiagnosticArgument(MaxLength = 36)] string playbackId)
    {
        try
        {
            InputTapePlayback playback = _inspection.GetPlayback(Owner(context), Guid.Parse(playbackId));
            return DiagnosticResult<InputTapeReceipt>.Success(TapeReceipt(playback), playback.Revision);
        }
        catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException)
        {
            return DiagnosticResult<InputTapeReceipt>.Reject("tape-rejected", exception.Message);
        }
    }

    [DiagnosticOperation(DiagnosticPermission.OverrideInput, Id = "live-begin", DisplayName = "Begin live Input", Traffic = DiagnosticOperationTraffic.Control)]
    private DiagnosticResult<InputLiveReceipt> BeginLive(DiagnosticOperationContext context, [DiagnosticArgument(MaxLength = 16384)] string targetIdsJson)
    {
        try
        {
            _inspection.BeginFrame(context.Frame.Number);
            InputLiveSession live = _inspection.Begin(Owner(context), ParseTargets(targetIdsJson));
            return DiagnosticResult<InputLiveReceipt>.Success(new(live.Id.ToString("D"), live.Revision), live.Revision);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException)
        {
            return DiagnosticResult<InputLiveReceipt>.Reject("live-rejected", exception.Message);
        }
    }

    [DiagnosticOperation(DiagnosticPermission.OverrideInput, Id = "live-send", DisplayName = "Send live Input", Traffic = DiagnosticOperationTraffic.Stream)]
    private DiagnosticResult<InputControlReceipt> SendLive(
        DiagnosticOperationContext context,
        [DiagnosticArgument(MaxLength = 36)] string liveSessionId,
        [DiagnosticArgument(Minimum = 0)] long batchSequence,
        [DiagnosticArgument(MaxLength = 1048576)] string valuesJson)
    {
        try
        {
            _inspection.BeginFrame(context.Frame.Number);
            _inspection.Send(Owner(context), Guid.Parse(liveSessionId), batchSequence, ParseLiveValues(valuesJson));
            return DiagnosticResult<InputControlReceipt>.Success(new(true));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or KeyNotFoundException or JsonException)
        {
            return DiagnosticResult<InputControlReceipt>.Reject("live-rejected", exception.Message);
        }
    }

    [DiagnosticOperation(DiagnosticPermission.OverrideInput, Id = "live-heartbeat", DisplayName = "Renew live Input", Traffic = DiagnosticOperationTraffic.Control)]
    private DiagnosticResult<InputControlReceipt> HeartbeatLive(DiagnosticOperationContext context, [DiagnosticArgument(MaxLength = 36)] string liveSessionId)
    {
        try
        {
            _inspection.BeginFrame(context.Frame.Number);
            _inspection.Heartbeat(Owner(context), Guid.Parse(liveSessionId));
            return DiagnosticResult<InputControlReceipt>.Success(new(true));
        }
        catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException)
        {
            return DiagnosticResult<InputControlReceipt>.Reject("live-rejected", exception.Message);
        }
    }

    [DiagnosticOperation(DiagnosticPermission.OverrideInput, Id = "live-stop", DisplayName = "Stop live Input", Traffic = DiagnosticOperationTraffic.Control)]
    private DiagnosticResult<InputControlReceipt> StopLive(DiagnosticOperationContext context, [DiagnosticArgument(MaxLength = 36)] string liveSessionId)
    {
        try
        {
            _inspection.BeginFrame(context.Frame.Number);
            _inspection.Stop(Owner(context), Guid.Parse(liveSessionId));
            return DiagnosticResult<InputControlReceipt>.Success(new(true));
        }
        catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException)
        {
            return DiagnosticResult<InputControlReceipt>.Reject("live-rejected", exception.Message);
        }
    }

    private static InputOverrideOwner Owner(DiagnosticOperationContext context) => new(context.SessionId, context.ActorId);

    private static DiagnosticResult<InputOverrideReceipt> OverrideResult(InputOverrideResult result)
    {
        if (!result.IsSuccess)
        {
            return DiagnosticResult<InputOverrideReceipt>.Reject(result.ErrorCode!, result.ErrorMessage!);
        }

        InputOverrideLease lease = result.Lease!;
        return DiagnosticResult<InputOverrideReceipt>.Success(new(lease.Id.ToString("D"), lease.Target.Id.ToString("D"), lease.Revision, lease.Remaining.TotalMilliseconds, lease.IsActive), lease.Revision);
    }

    private static InputTapeReceipt TapeReceipt(InputTapePlayback playback) => new(playback.Id.ToString("D"), playback.Revision, playback.CommandIndex, playback.IsRunning, playback.StopReason ?? string.Empty);

    private static InputOverrideTarget[] ParseTargets(string json)
        => JsonSerializer.Deserialize<string[]>(json)?.Select(value => new InputOverrideTarget(Guid.Parse(value))).ToArray() ?? throw new JsonException("Target array is required.");

    private static InputLiveValue[] ParseLiveValues(string json)
        => JsonSerializer.Deserialize<LiveWire[]>(json)?.Select(value => new InputLiveValue(new(Guid.Parse(value.Target)), new(value.X, value.Y))).ToArray() ?? throw new JsonException("Value array is required.");

    private static InputTape ParseTape(string json)
    {
        TapeWire wire = JsonSerializer.Deserialize<TapeWire>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new JsonException("Tape is required.");
        return new(
            wire.Version,
            wire.Tracks.Select(track => new InputTapeTrack(track.Alias, Enum.Parse<ActionValueKind>(track.Kind, true))).ToImmutableArray(),
            wire.Commands.Select(command => new InputTapeCommand(Enum.Parse<InputTapeCommandKind>(command.Kind, true), command.Track, new((float)command.X, (float)command.Y), command.Frames)).ToImmutableArray());
    }

    private sealed record LiveWire(string Target, float X, float Y);

    private sealed record TapeWire(int Version, TapeTrackWire[] Tracks, TapeCommandWire[] Commands);

    private sealed record TapeTrackWire(string Alias, string Kind);

    private sealed record TapeCommandWire(string Kind, int Track, float X, float Y, long Frames);
}

#pragma warning restore SA1204
