using Lumyte.Input.Abstractions;

namespace Lumyte.Input;

/// <summary>Associates normalized input with its device, sequence and monotonic receipt time.</summary>
/// <param name="DeviceId">The DeviceId value.</param>
/// <param name="Sequence">The system-wide increasing record sequence.</param>
/// <param name="RecordedAt">Monotonic elapsed receipt time since the input system was created.</param>
/// <param name="Data">The Data value.</param>
public readonly record struct InputRecord(InputDeviceId DeviceId, ulong Sequence, TimeSpan RecordedAt, InputData Data)
{
    /// <summary>Gets the acquisition origin; null means unknown or windowless.</summary>
    public InputWindowId? WindowId { get; init; }

    /// <summary>Gets the scope owning this physical series.</summary>
    public InputWindowId? StateWindowId { get; init; }

    /// <summary>Gets the original acquisition cause, or null for synthetic records.</summary>
    public InputCaptureStamp? Capture { get; init; }
}
