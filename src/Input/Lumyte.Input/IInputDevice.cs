namespace Lumyte.Input;

/// <summary>Provides normalized input for one device.</summary>
public interface IInputDevice : IDisposable
{
    /// <summary>Gets the device descriptor.</summary>
    InputDeviceDescriptor Descriptor { get; }

    /// <summary>Drains pending input in receive order without consuming on failure.</summary>
    /// <returns>An immutable batch of normalized input data.</returns>
    IReadOnlyList<InputData> DrainEvents();
}
