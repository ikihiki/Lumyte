namespace Lumyte.Input.Processing;

/// <summary>Transforms a device batch before InputSystem records it.</summary>
public interface IDeviceDataProcessor
{
    /// <summary>Corrects a normalized input batch.</summary>
    /// <remarks>
    /// Return an immutable batch. If processing fails, leave internal state unchanged so the same
    /// batch and timestamp can be retried. Completed earlier pipeline stages are not replayed.
    /// </remarks>
    /// <param name = "data">The data value.</param>
    /// <param name = "now">The now value.</param>
    /// <returns>The result of the operation.</returns>
    IReadOnlyList<InputData> Process(IReadOnlyList<InputData> data, TimeSpan now);

    /// <summary>Discards transient recognition or correction state.</summary>
    /// <remarks>If resetting fails, leave internal state unchanged so the operation can be retried.</remarks>
    void Reset();
}
