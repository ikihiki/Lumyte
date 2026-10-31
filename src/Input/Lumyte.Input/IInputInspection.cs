using Lumyte.Input.Abstractions;

namespace Lumyte.Input;

/// <summary>Observes every source and device owned by one input system.</summary>
public interface IInputInspection
{
    /// <summary>Registers an input system before its sources are initialized.</summary>
    /// <param name="system">The input system.</param>
    void SystemCreated(InputSystem system);

    /// <summary>Observes source initialization.</summary>
    /// <param name="system">The input system.</param>
    /// <param name="source">The source being initialized.</param>
    void SourceInitializing(InputSystem system, IInputSource source);

    /// <summary>Observes completion of one source update.</summary>
    /// <param name="system">The input system.</param>
    /// <param name="source">The updated source.</param>
    void SourceUpdated(InputSystem system, IInputSource source);

    /// <summary>Observes source shutdown.</summary>
    /// <param name="system">The input system.</param>
    /// <param name="source">The source being shut down.</param>
    void SourceShutdown(InputSystem system, IInputSource source);

    /// <summary>Registers a device discovered by a source.</summary>
    /// <param name="system">The input system.</param>
    /// <param name="device">The scoped device identifier.</param>
    /// <param name="descriptor">The device metadata.</param>
    void Registered(InputSystem system, InputDeviceId device, InputDeviceDescriptor descriptor);

    /// <summary>Transforms one device batch before state, history, and notifications are updated.</summary>
    /// <param name="system">The input system.</param>
    /// <param name="device">The scoped device identifier.</param>
    /// <param name="data">The physical device batch.</param>
    /// <param name="now">The current input-system time.</param>
    /// <returns>The effective batch. Returning <paramref name="data"/> avoids an allocation.</returns>
    IReadOnlyList<InputData> Process(InputSystem system, InputDeviceId device, IReadOnlyList<InputData> data, TimeSpan now) => data;

    /// <summary>Transforms an ordered batch after physical ownership is resolved.</summary>
    /// <param name="system">The system.</param>
    /// <param name="streamId">The acquisition stream.</param>
    /// <param name="events">The immutable scoped events.</param>
    /// <param name="now">The batch time.</param>
    /// <returns>The effective events with unchanged metadata.</returns>
    IReadOnlyList<InputScopedEvent> ProcessEvents(InputSystem system, Guid streamId, IReadOnlyList<InputScopedEvent> events, TimeSpan now) => events;

    /// <summary>Receives the immutable current state after a device has been processed.</summary>
    /// <param name="system">The input system.</param>
    /// <param name="device">The scoped device identifier.</param>
    /// <param name="state">The current immutable state.</param>
    /// <param name="latestSequence">The latest system sequence.</param>
    void DeviceUpdated(InputSystem system, InputDeviceId device, InputDeviceState state, ulong latestSequence);

    /// <summary>Observes removal after the disconnected state has been published.</summary>
    /// <param name="system">The input system.</param>
    /// <param name="device">The scoped device identifier.</param>
    void Unregistered(InputSystem system, InputDeviceId device);

    /// <summary>Removes every target belonging to a disposed system.</summary>
    /// <param name="system">The input system.</param>
    void SystemDisposed(InputSystem system);
}
