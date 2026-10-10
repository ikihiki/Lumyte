namespace Lumyte.Input;

/// <summary>Provides source-scoped device registration.</summary>
public interface IInputDeviceRegistry
{
    /// <summary>Registers a device and transfers its ownership on success.</summary>
    /// <param name="device">The device to register.</param>
    /// <returns>A system-wide unique identifier.</returns>
    InputDeviceId RegisterDevice(IInputDevice device);

    /// <summary>Schedules final input collection and device removal.</summary>
    /// <param name="device">The identifier belonging to this source.</param>
    void UnregisterDevice(InputDeviceId device);
}
