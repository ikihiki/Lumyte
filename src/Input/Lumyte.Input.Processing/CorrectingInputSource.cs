namespace Lumyte.Input.Processing;

/// <summary>Decorates a borrowed source and transfers corrected devices to InputSystem.</summary>
/// <remarks>Initializes a new instance of the <see cref = "CorrectingInputSource"/> class.</remarks>
/// <param name = "inner">The inner value.</param>
/// <param name = "factory">The factory value.</param>
/// <param name = "getTime">The getTime value.</param>
public sealed class CorrectingInputSource(IInputSource inner, Func<InputDeviceDescriptor, IEnumerable<IDeviceDataProcessor>> factory, Func<TimeSpan> getTime) : IInputSource, IInputDeviceRegistry
{
    private readonly IInputSource _inner = inner;
    private readonly Func<TimeSpan> _getTime = getTime;
    private readonly Dictionary<InputDeviceId, CorrectedInputDevice> _devices = new();
    private Func<InputDeviceDescriptor, IEnumerable<IDeviceDataProcessor>> _factory = factory;
    private IInputDeviceRegistry? _registry;

    /// <summary>Connects this source to its scoped registry.</summary>
    /// <param name = "registry">The registry value.</param>
    public void Initialize(IInputDeviceRegistry registry)
    {
        _registry = registry;
        _inner.Initialize(this);
    }

    /// <summary>Prepares the next device input batch.</summary>
    public void Update() => _inner.Update();

    /// <summary>Stops the borrowed source without taking its device ownership.</summary>
    public void Shutdown() => _inner.Shutdown();

    /// <summary>Releases owned device resources.</summary>
    public void Dispose()
    {
    }

    /// <summary>Prepares and schedules per-device pipelines for the next update.</summary>
    /// <param name = "factory">The factory value.</param>
    public void SetProcessorFactory(Func<InputDeviceDescriptor, IEnumerable<IDeviceDataProcessor>> factory)
    {
        (CorrectedInputDevice Device, IDeviceDataProcessor[] Processors)[] prepared = _devices.Values.Select(device => (Device: device, Processors: factory(device.Descriptor).ToArray())).ToArray();
        foreach ((CorrectedInputDevice Device, IDeviceDataProcessor[] Processors) item in prepared)
        {
            item.Device.SetProcessors(item.Processors);
        }

        _factory = factory;
    }

    /// <summary>Registers a device, transferring ownership only after success.</summary>
    /// <param name = "device">The device value.</param>
    /// <returns>The result of the operation.</returns>
    public InputDeviceId RegisterDevice(IInputDevice device)
    {
        var wrapped = new CorrectedInputDevice(device, _factory(device.Descriptor), _getTime);
        InputDeviceId id = (_registry ?? throw new InvalidOperationException("Source is not initialized.")).RegisterDevice(wrapped);
        _devices.Add(id, wrapped);
        return id;
    }

    /// <summary>Schedules removal of the physical device and its derived devices.</summary>
    /// <param name = "device">The device value.</param>
    public void UnregisterDevice(InputDeviceId device)
    {
        (_registry ?? throw new InvalidOperationException("Source is not initialized.")).UnregisterDevice(device);
        _devices.Remove(device);
    }
}
