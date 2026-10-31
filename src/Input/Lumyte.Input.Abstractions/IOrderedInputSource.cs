namespace Lumyte.Input.Abstractions;

/// <summary>Acquires one stable, acknowledged stream across all devices.</summary>
public interface IOrderedInputSource : IInputSource
{
    /// <summary>Gets the immutable, nonempty stream identity.</summary>
    Guid StreamId { get; }

    /// <summary>Reads the immutable first unacknowledged batch, or null.</summary>
    /// <returns>The first batch, or null when none is pending.</returns>
    InputEventBatch? PeekEvents();

    /// <summary>Acknowledges the first batch. Repeating the last acknowledgment is harmless.</summary>
    /// <param name="batchId">The batch identity.</param>
    void CommitEvents(ulong batchId);

    /// <summary>Permanently stops acquisition and discards unacknowledged batches.</summary>
    void AbortEvents();
}
