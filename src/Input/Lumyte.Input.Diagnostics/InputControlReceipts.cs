using Lumyte.Diagnostics;

namespace Lumyte.Input.Diagnostics;

#pragma warning disable SA1402, SA1649 // Scalar operation receipts form one wire contract.

/// <summary>Returns one direct override lease.</summary>
public sealed record InputOverrideReceipt(string LeaseId, string TargetId, long Revision, double RemainingMs, bool Active);

/// <summary>Returns one Tape playback cursor.</summary>
public sealed record InputTapeReceipt(string PlaybackId, long Revision, long CommandIndex, bool Running, [property: DiagnosticMember(MaxLength = 64)] string StopReason);

/// <summary>Returns one live producer.</summary>
public sealed record InputLiveReceipt(string SessionId, long Revision);

/// <summary>Returns a successful mutation without exposing mutable state.</summary>
public sealed record InputControlReceipt(bool Accepted);

#pragma warning restore SA1402, SA1649
