namespace Lumyte.Input;

/// <summary>Represents DeviceConnectedData.</summary>
/// <param name="Info">The Info value.</param>
public sealed record DeviceConnectedData(InputDeviceInfo Info) : InputData;
