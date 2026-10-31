namespace Lumyte.Input.Abstractions;

/// <summary>An immutable ordered acquisition boundary, including boundaries that were filtered empty.</summary>
public sealed class InputEventBatch
{
    /// <summary>Initializes a new instance of the <see cref="InputEventBatch"/> class by validating and copying events.</summary>
    /// <param name="id">The positive batch ID.</param>
    /// <param name="capturedThrough">The acquired tail.</param>
    /// <param name="events">Events in acquisition order.</param>
    public InputEventBatch(ulong id, ulong capturedThrough, IReadOnlyList<InputSourceEvent> events)
    {
        ArgumentOutOfRangeException.ThrowIfZero(id);
        ArgumentNullException.ThrowIfNull(events);
        var copy = new InputSourceEvent[events.Count];
        for (int i = 0; i < copy.Length; i++)
        {
            InputSourceEvent item = events[i];
            ArgumentNullException.ThrowIfNull(item);
            ArgumentNullException.ThrowIfNull(item.Data);
            if (item.DeviceId.Value == 0 || item.OriginDeviceId?.Value == 0 || item.WindowId?.Value == Guid.Empty || item.StateWindowId?.Value == Guid.Empty || (item.OriginDeviceId is null && item.StateWindowId is not null) || item.CaptureSequence == 0 || item.CaptureSequence > capturedThrough)
            {
                throw new ArgumentException("Invalid event identity or acquisition boundary.", nameof(events));
            }

            copy[i] = item;
        }

        Id = id;
        CapturedThrough = capturedThrough;
        Events = Array.AsReadOnly(copy);
    }

    /// <summary>Gets the batch ID.</summary>
    public ulong Id { get; }

    /// <summary>Gets the acquired tail, including filtered events.</summary>
    public ulong CapturedThrough { get; }

    /// <summary>Gets the immutable ordered events.</summary>
    public IReadOnlyList<InputSourceEvent> Events { get; }
}
