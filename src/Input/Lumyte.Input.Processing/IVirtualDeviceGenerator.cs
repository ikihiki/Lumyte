namespace Lumyte.Input.Processing;

/// <summary>Produces logical controller input from one physical device batch.</summary>
public interface IVirtualDeviceGenerator
{
    /// <summary>Generates logical input for this physical batch.</summary>
    /// <param name = "origin">The origin value.</param>
    /// <param name = "data">The data value.</param>
    /// <param name = "now">The now value.</param>
    /// <returns>The result of the operation.</returns>
    IReadOnlyList<InputData> Generate(InputDeviceId origin, IReadOnlyList<InputData> data, TimeSpan now);

    /// <summary>Discards transient recognition or correction state.</summary>
    /// <param name = "origin">The origin value.</param>
    /// <param name = "now">The now value.</param>
    void Reset(InputDeviceId origin, TimeSpan now);
}
