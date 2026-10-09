namespace Lumyte.Input.Processing;

/// <summary>Transforms a device batch before InputSystem records it.</summary>
public interface IDeviceDataProcessor
{
    /// <summary>Corrects a normalized input batch.</summary>
    /// <param name = "data">The data value.</param>
    /// <param name = "now">The now value.</param>
    /// <returns>The result of the operation.</returns>
    IReadOnlyList<InputData> Process(IReadOnlyList<InputData> data, TimeSpan now);

    /// <summary>Discards transient recognition or correction state.</summary>
    void Reset();
}
