namespace Lumyte.Input.Abstractions;

/// <summary>An acquired event with its immutable origin and causal sequence.</summary>
public sealed record InputSourceEvent(InputDeviceId DeviceId, InputWindowId? WindowId, ulong? CaptureSequence, InputData Data)
{
    /// <summary>Gets the preceding causal device for a derived event; null identifies a direct acquisition.</summary>
    public InputDeviceId? OriginDeviceId { get; init; }

    /// <summary>Gets the processing owner of a derived event; null is an explicit windowless scope.</summary>
    public InputWindowId? StateWindowId { get; init; }
}
