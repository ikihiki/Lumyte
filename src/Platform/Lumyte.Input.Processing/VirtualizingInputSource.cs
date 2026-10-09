namespace Lumyte.Input.Processing;

/// <summary>Acquires each physical batch once and prepares physical and virtual devices in the same update.</summary>
/// <remarks>Initializes a new instance of the <see cref = "VirtualizingInputSource"/> class.</remarks>
/// <param name = "inner">The inner value.</param>
/// <param name = "factory">The factory value.</param>
/// <param name = "getTime">The getTime value.</param>
public sealed class VirtualizingInputSource(IInputSource inner, Func<InputDeviceDescriptor, IVirtualDeviceGenerator?> factory, Func<TimeSpan> getTime) : IInputSource, IInputDeviceRegistry
{
    private readonly IInputSource _inner = inner;
    private readonly Func<TimeSpan> _getTime = getTime;
    private readonly Dictionary<InputDeviceId, Pair> _pairs = new();
    private Func<InputDeviceDescriptor, IVirtualDeviceGenerator?> _factory = factory;
    private Dictionary<InputDeviceId, IVirtualDeviceGenerator>? _pending;
    private IInputDeviceRegistry? _registry;

    /// <summary>Schedules replacement generators for the next source update.</summary>
    /// <param name = "factory">Creates one independent generator per physical device.</param>
    public void SetGeneratorFactory(Func<InputDeviceDescriptor, IVirtualDeviceGenerator?> factory)
    {
        var prepared = _pairs.ToDictionary(pair => pair.Key, pair => factory(pair.Value.Raw.Descriptor) ?? throw new ArgumentException("Existing virtual devices require a generator.", nameof(factory)));
        _pending = prepared;
        _factory = factory;
    }

    /// <summary>Connects this source to its scoped registry.</summary>
    /// <param name = "registry">The registry value.</param>
    public void Initialize(IInputDeviceRegistry registry)
    {
        _registry = registry;
        _inner.Initialize(this);
    }

    /// <summary>Prepares the next device input batch.</summary>
    public void Update()
    {
        if (_pending is not null)
        {
            foreach (KeyValuePair<InputDeviceId, IVirtualDeviceGenerator> item in _pending)
            {
                if (!_pairs.TryGetValue(item.Key, out Pair? pair))
                {
                    continue;
                }

                pair.Generator.Reset(item.Key, _getTime());
                pair.Virtual.Enqueue(new InputData[] { new FocusData(false) });
                pair.Virtual.Enqueue(item.Value.Generate(item.Key, new InputData[] { new FocusData(pair.Focused) }, _getTime()));
                _pairs[item.Key] = pair with
                {
                    Generator = item.Value,
                };
            }

            _pending = null;
        }

        _inner.Update();
        foreach (KeyValuePair<InputDeviceId, Pair> item in _pairs)
        {
            Acquire(item.Key, item.Value);
        }
    }

    /// <summary>Stops the borrowed source without taking its device ownership.</summary>
    public void Shutdown()
    {
        _inner.Shutdown();
        foreach (KeyValuePair<InputDeviceId, Pair> item in _pairs)
        {
            Acquire(item.Key, item.Value);
        }
    }

    /// <summary>Releases owned device resources.</summary>
    public void Dispose()
    {
    }

    /// <summary>Registers a device, transferring ownership only after success.</summary>
    /// <param name = "device">The device value.</param>
    /// <returns>The result of the operation.</returns>
    public InputDeviceId RegisterDevice(IInputDevice device)
    {
        IInputDeviceRegistry registry = _registry ?? throw new InvalidOperationException("Source is not initialized.");
        IVirtualDeviceGenerator? generator = _factory(device.Descriptor);
        if (generator is null)
        {
            return registry.RegisterDevice(device);
        }

        var physical = new BufferedDevice(device.Descriptor, device);
        InputDeviceId id = registry.RegisterDevice(physical);
        var virtualDevice = new BufferedDevice(new InputDeviceDescriptor(InputDeviceKind.Controller, device.Descriptor.Name + " gestures", InputDeviceIdentityKind.Logical), null);
        try
        {
            InputDeviceId virtualId = registry.RegisterDevice(virtualDevice);
            _pairs.Add(id, new Pair(device, physical, virtualDevice, virtualId, generator));
            return id;
        }
        catch
        {
            physical.DetachOwner();
            registry.UnregisterDevice(id);
            throw;
        }
    }

    /// <summary>Schedules removal of the physical device and its derived devices.</summary>
    /// <param name = "device">The device value.</param>
    public void UnregisterDevice(InputDeviceId device)
    {
        IInputDeviceRegistry registry = _registry ?? throw new InvalidOperationException("Source is not initialized.");
        if (_pairs.Remove(device, out Pair? pair))
        {
            Acquire(device, pair);
            pair.Generator.Reset(device, _getTime());
            registry.UnregisterDevice(pair.VirtualId);
        }

        registry.UnregisterDevice(device);
    }

    private void Acquire(InputDeviceId id, Pair pair)
    {
        IReadOnlyList<InputData> data = pair.Raw.DrainEvents();
        foreach (FocusData focus in data.OfType<FocusData>())
        {
            pair.Focused = focus.IsFocused;
        }

        pair.Physical.Enqueue(data);
        pair.Virtual.Enqueue(pair.Generator.Generate(id, data, _getTime()));
    }

    private sealed record Pair(IInputDevice Raw, BufferedDevice Physical, BufferedDevice Virtual, InputDeviceId VirtualId, IVirtualDeviceGenerator Generator)
    {
        public bool Focused { get; set; }
    }

    private sealed class BufferedDevice(InputDeviceDescriptor descriptor, IInputDevice? owner) : IInputDevice
    {
        private readonly List<InputData> _queue = new();
        private IInputDevice? _owner = owner;

        public InputDeviceDescriptor Descriptor { get; } = descriptor;

        public void Enqueue(IEnumerable<InputData> data) => _queue.AddRange(data);

        public IReadOnlyList<InputData> DrainEvents()
        {
            InputData[] data = _queue.ToArray();
            _queue.Clear();
            return data;
        }

        public void DetachOwner() => _owner = null;

        public void Dispose()
        {
            IInputDevice? device = _owner;
            _owner = null;
            device?.Dispose();
        }
    }
}
