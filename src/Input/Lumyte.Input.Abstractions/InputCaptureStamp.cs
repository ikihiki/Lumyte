namespace Lumyte.Input.Abstractions;

/// <summary>Identifies the acquisition that caused a record, independently of receipt sequence.</summary>
public readonly record struct InputCaptureStamp(Guid StreamId, ulong Sequence);
