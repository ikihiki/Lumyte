using Lumyte.Input.Abstractions;
using Lumyte.Input.Internal;

namespace Lumyte.Input.Processing;

internal sealed class OrderedPipeline(IOrderedInputSource inner, Func<InputDeviceDescriptor, InputWindowId?, object[]> factory, Func<TimeSpan> getTime, bool virtualizing) : IInputDeviceRegistry
{
    private readonly List<InputSourceEvent> _output = [];
    private readonly HashSet<InputDeviceId> _removals = [];
    private readonly HashSet<(InputDeviceId Device, int Kind, ulong Pointer)> _blockedPointers = [];
    private readonly IOrderedInputSource _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly Func<TimeSpan> _getTime = getTime ?? throw new ArgumentNullException(nameof(getTime));
    private readonly Dictionary<InputDeviceId, (InputDeviceDescriptor Descriptor, InputDeviceId Virtual)> _devices = [];
    private readonly Dictionary<(InputDeviceId Device, Guid Window), Scope> _scopes = [];
    private readonly HashSet<object> _instances = new(ReferenceEqualityComparer.Instance);
    private Func<InputDeviceDescriptor, InputWindowId?, object[]> _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    private Dictionary<(InputDeviceId Device, Guid Window), Scope>? _replacement;
    private Func<InputDeviceDescriptor, InputWindowId?, object[]>? _replacementFactory;
    private InputSeriesOwners _owners = new();
    private IInputDeviceRegistry? _registry;
    private InputEventBatch? _raw;
    private InputEventBatch? _ready;
    private InputSourceEvent? _current;
    private Scope? _scope;
    private IReadOnlyList<InputData> _data = [];
    private int _event;
    private int _stage;
    private bool _reset;
    private bool _gestureCanceled;
    private bool _aborted;
    private bool _disposed;
    private bool _shutdown;
    private bool _clockPending;
    private (InputDeviceId Device, Guid Window)[]? _clockScopes;
    private int _clockScope;
    private TimeSpan _time;
    private ulong _id;
    private ulong _committed;
    private ulong _captured;

    internal Guid StreamId => _inner.StreamId;

    public InputDeviceId RegisterDevice(IInputDevice device)
    {
        IInputDeviceRegistry registry = _registry ?? throw new InvalidOperationException("Source is not initialized.");
        InputDeviceId id = registry.RegisterDevice(device);
        InputDeviceId derived = default;
        if (virtualizing)
        {
            derived = registry.RegisterDevice(new VirtualDevice(new(InputDeviceKind.Controller, device.Descriptor.Name + " gestures", InputDeviceIdentityKind.Logical)));
        }

        _devices.Add(id, (device.Descriptor, derived));
        return id;
    }

    public void UnregisterDevice(InputDeviceId device)
    {
        if (!_devices.ContainsKey(device) || !_removals.Add(device))
        {
            throw new ArgumentException("The device is unknown or already scheduled for removal.", nameof(device));
        }
    }

    internal void Initialize(IInputDeviceRegistry registry)
    {
        if (_registry is not null || _aborted)
        {
            throw new InvalidOperationException("This connection cannot be initialized again.");
        }

        _registry = registry;
        _inner.Initialize(this);
    }

    internal void Update()
    {
        if (_aborted || _shutdown)
        {
            return;
        }

        ApplyRemovals();
        _inner.Update();
        ApplyRemovals();
        _clockPending = true;
    }

    internal void SetFactory(Func<InputDeviceDescriptor, InputWindowId?, object[]> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var seen = new HashSet<object>(_instances, ReferenceEqualityComparer.Instance);
        var prepared = new Dictionary<(InputDeviceId Device, Guid Window), Scope>();
        foreach (KeyValuePair<(InputDeviceId Device, Guid Window), Scope> pair in _scopes)
        {
            prepared.Add(pair.Key, CreateScope(factory, pair.Key, seen));
        }

        _replacement = prepared;
        _replacementFactory = factory;
    }

    internal InputEventBatch? PeekEvents()
    {
        if (_aborted)
        {
            return null;
        }

        if (_ready is not null)
        {
            return _ready;
        }

        if (_raw is null && _clockScopes is null)
        {
            _time = _getTime();
            ApplyReplacement();
            _raw = _inner.PeekEvents();
            if (_raw is null)
            {
                if (!_clockPending)
                {
                    return null;
                }

                _clockPending = false;
                _clockScopes = _scopes.Keys.ToArray();
                _clockScope = 0;
            }
        }

        if (_raw is not null)
        {
            while (_event < _raw.Events.Count)
            {
                if (_current is null)
                {
                    InputSourceEvent current = _raw.Events[_event];
                    InputSeriesOwners owners = _owners.Clone();
                    (InputWindowId? owner, bool suppress) = owners.Resolve(current);
                    (InputDeviceId Device, Guid Window) key = (current.DeviceId, owner?.Value ?? Guid.Empty);
                    _scope = GetScope(key);
                    _owners = owners;
                    _current = current;
                    (int kind, ulong pointer) = Pointer(current.Data);
                    (InputDeviceId Device, int Kind, ulong Pointer) identity = (current.DeviceId, kind, pointer);
                    bool crossed = virtualizing && kind != 0 && owner != current.WindowId;
                    _gestureCanceled = crossed && _blockedPointers.Add(identity);
                    bool blocked = virtualizing && _blockedPointers.Contains(identity);
                    _data = blocked || (suppress && !IsTerminal(current.Data)) ? [] : new[] { current.Data };
                    _stage = 0;
                    _reset = false;
                }

                CompleteEvent();
                if (IsTerminal(_current!.Data))
                {
                    (int kind, ulong pointer) = Pointer(_current.Data);
                    _blockedPointers.Remove((_current.DeviceId, kind, pointer));
                }

                _event++;
                _current = null;
            }

            _captured = _raw.CapturedThrough;
        }
        else
        {
            while (_clockScope < _clockScopes!.Length)
            {
                if (_current is null)
                {
                    (InputDeviceId Device, Guid Window) key = _clockScopes[_clockScope];
                    _current = new(key.Device, key.Window == Guid.Empty ? null : new(key.Window), null, new FocusData(true));
                    _scope = _scopes[key];
                    _data = [];
                    _stage = 0;
                    _reset = true;
                    _gestureCanceled = false;
                }

                CompleteEvent();
                _clockScope++;
                _current = null;
            }
        }

        _ready = new(checked(++_id), _captured, _output);
        return _ready;
    }

    internal void CommitEvents(ulong id)
    {
        if (id == _committed && id != 0)
        {
            return;
        }

        if (_ready?.Id != id)
        {
            throw new ArgumentException("Only the first batch can be acknowledged.", nameof(id));
        }

        if (_raw is not null)
        {
            _inner.CommitEvents(_raw.Id);
        }

        _committed = id;
        _raw = null;
        _ready = null;
        _output.Clear();
        _event = 0;
        _clockScopes = null;
    }

    internal void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_registry is not null && !_shutdown && !_aborted)
        {
            throw new InvalidOperationException("Stop the consumer before disposing its processing source.");
        }

        _disposed = true;
        var errors = new List<Exception>();
        (InputDeviceId Device, object Step)[] steps = _scopes.Values.Concat(_replacement?.Values ?? Enumerable.Empty<Scope>()).SelectMany(scope => scope.Steps.Select(step => (scope.Device, Step: step))).DistinctBy(item => item.Step, ReferenceEqualityComparer.Instance).ToArray();
        foreach ((InputDeviceId device, object step) in steps)
        {
            try
            {
                if (step is IDeviceDataProcessor processor)
                {
                    processor.Reset();
                }
                else if (step is IVirtualDeviceGenerator generator)
                {
                    generator.Reset(device, _time);
                }
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
        }

        _scopes.Clear();
        _replacement?.Clear();
        _devices.Clear();
        _instances.Clear();
        _blockedPointers.Clear();
        _output.Clear();
        _raw = null;
        _ready = null;
        _current = null;
        if (errors.Count != 0)
        {
            throw new AggregateException("Processing scope disposal completed with errors.", errors);
        }
    }

    internal void Shutdown()
    {
        if (!_shutdown)
        {
            _shutdown = true;
            _inner.Shutdown();
        }
    }

    internal void AbortEvents()
    {
        if (_aborted)
        {
            return;
        }

        _aborted = true;
        _raw = null;
        _ready = null;
        _current = null;
        _output.Clear();
        _inner.AbortEvents();
    }

    private static (int Kind, ulong Pointer) Pointer(InputData data) => data switch
    {
        TouchData touch => (1, touch.ContactId.Value),
        PenData pen => (2, pen.PointerId.Value),
        _ => (0, 0UL),
    };

    private static bool IsTerminal(InputData data) => data is KeyData { IsDown: false } or MouseButtonData { IsDown: false } or MouseButtonCanceledData or TouchData { Phase: TouchPhase.Ended or TouchPhase.Canceled } or PenData { Phase: PenPhase.Left or PenPhase.Canceled };

    private void ApplyRemovals()
    {
        if (_raw is not null || _ready is not null || _inner.PeekEvents() is not null)
        {
            return;
        }

        foreach (InputDeviceId device in _removals.ToArray())
        {
            (InputDeviceDescriptor Descriptor, InputDeviceId Virtual) pair = _devices[device];
            _registry!.UnregisterDevice(device);
            if (pair.Virtual.Value != 0)
            {
                _registry.UnregisterDevice(pair.Virtual);
            }

            _devices.Remove(device);
            _removals.Remove(device);
            foreach ((InputDeviceId Device, Guid Window) scope in _scopes.Keys.Where(scope => scope.Device == device).ToArray())
            {
                _scopes.Remove(scope);
            }
        }
    }

    private Scope GetScope((InputDeviceId Device, Guid Window) key)
    {
        if (!_scopes.TryGetValue(key, out Scope? scope))
        {
            scope = CreateScope(_factory, key, _instances);
            _scopes.Add(key, scope);
            if (_replacement is not null)
            {
                var seen = new HashSet<object>(_instances, ReferenceEqualityComparer.Instance);
                seen.UnionWith(_replacement.Values.SelectMany(value => value.Steps));
                _replacement.Add(key, CreateScope(_replacementFactory!, key, seen));
            }
        }

        return scope;
    }

    private Scope CreateScope(Func<InputDeviceDescriptor, InputWindowId?, object[]> factory, (InputDeviceId Device, Guid Window) key, HashSet<object> seen)
    {
        object[] steps = factory(_devices[key.Device].Descriptor, key.Window == Guid.Empty ? null : new(key.Window));
        var unique = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (object step in steps)
        {
            if (step is not (IDeviceDataProcessor or IVirtualDeviceGenerator) || seen.Contains(step) || !unique.Add(step))
            {
                throw new ArgumentException("Each scope requires independent nonnull processor/generator instances.");
            }
        }

        seen.UnionWith(unique);
        return new(steps, key.Device, key.Window == Guid.Empty ? null : new(key.Window));
    }

    private void CompleteEvent()
    {
        Scope scope = _scope!;
        InputSourceEvent original = _current!;
        if (_raw is not null && original.Data is FocusData focus)
        {
            scope.Focused = focus.IsFocused;
        }

        bool interruption = _gestureCanceled || original.Data is FocusData { IsFocused: false } or WindowInputClosedData or MouseButtonCanceledData;
        if (virtualizing)
        {
            if (!interruption && _data.Count != 0 && !scope.GeneratorFocused && _devices[original.DeviceId].Descriptor.Kind is InputDeviceKind.Mouse or InputDeviceKind.Touch or InputDeviceKind.Pen && _data[0] is not FocusData)
            {
                _data = new InputData[] { new FocusData(true) }.Concat(_data).ToArray();
            }

            if (!_reset && interruption && scope.Steps.FirstOrDefault() is IVirtualDeviceGenerator reset)
            {
                reset.Reset(original.DeviceId, _time);
            }

            _reset = true;
            if (_stage == 0)
            {
                IReadOnlyList<InputData> generated = scope.Steps.FirstOrDefault() is IVirtualDeviceGenerator generator
                    ? generator.Generate(original.DeviceId, _data, _time) ?? throw new ArgumentException("A generator returned null.")
                    : [];
                if (interruption)
                {
                    scope.GeneratorFocused = false;
                }
                else if (_data.Any(data => data is FocusData { IsFocused: true }))
                {
                    scope.GeneratorFocused = true;
                }

                _data = generated;
                _stage = 1;
            }

            if (_raw is not null)
            {
                _output.Add(original);
            }

            InputDeviceId derived = _devices[original.DeviceId].Virtual;
            if (_raw is not null && interruption)
            {
                _output.Add(new(derived, scope.Window, null, original.Data is WindowInputClosedData ? new WindowInputClosedData() : new FocusData(false)) { OriginDeviceId = original.DeviceId, StateWindowId = scope.Window });
                scope.VirtualFocused = false;
            }
            else if ((_data.Count != 0 || (_raw is not null && original.Data is FocusData { IsFocused: true })) && !scope.VirtualFocused)
            {
                _output.Add(new(derived, scope.Window, null, new FocusData(true)) { OriginDeviceId = original.DeviceId, StateWindowId = scope.Window });
                scope.VirtualFocused = true;
            }

            foreach (InputData data in _data)
            {
                _output.Add(original with { DeviceId = derived, OriginDeviceId = original.DeviceId, StateWindowId = scope.Window, Data = data });
            }
        }
        else
        {
            while (_stage < scope.Steps.Length)
            {
                var processor = (IDeviceDataProcessor)scope.Steps[_stage];
                if (!_reset && interruption)
                {
                    processor.Reset();
                }

                _reset = true;
                _data = processor.Process(_data, _time) ?? throw new ArgumentException("A processor returned null.");
                _stage++;
                _reset = false;
            }

            if (_raw is not null && interruption && !_data.Contains(original.Data))
            {
                _output.Add(original);
            }

            foreach (InputData data in _data)
            {
                _output.Add(original with { Data = data });
            }
        }
    }

    private void ApplyReplacement()
    {
        if (_replacement is null)
        {
            return;
        }

        foreach (KeyValuePair<(InputDeviceId Device, Guid Window), Scope> pair in _scopes)
        {
            Scope old = pair.Value;
            while (old.ResetIndex < old.Steps.Length)
            {
                switch (old.Steps[old.ResetIndex])
                {
                    case IDeviceDataProcessor processor:
                        processor.Reset();
                        break;
                    case IVirtualDeviceGenerator generator:
                        generator.Reset(pair.Key.Device, _time);
                        break;
                }

                old.ResetIndex++;
            }
        }

        foreach (KeyValuePair<(InputDeviceId Device, Guid Window), Scope> pair in _scopes)
        {
            InputWindowId? window = pair.Key.Window == Guid.Empty ? null : new(pair.Key.Window);
            InputDeviceId device = virtualizing ? _devices[pair.Key.Device].Virtual : pair.Key.Device;
            _output.Add(new(device, window, null, new FocusData(false)));
            if (pair.Value.Focused)
            {
                _output.Add(new(device, window, null, new FocusData(true)));
            }
        }

        _factory = _replacementFactory!;
        _replacementFactory = null;
        _scopes.Clear();
        _instances.Clear();
        foreach (KeyValuePair<(InputDeviceId Device, Guid Window), Scope> pair in _replacement)
        {
            _scopes.Add(pair.Key, pair.Value);
            _instances.UnionWith(pair.Value.Steps);
        }

        _replacement = null;
    }

    private sealed class Scope(object[] steps, InputDeviceId device, InputWindowId? window)
    {
        internal object[] Steps { get; } = steps;

        internal InputDeviceId Device { get; } = device;

        internal InputWindowId? Window { get; } = window;

        internal int ResetIndex { get; set; }

        internal bool VirtualFocused { get; set; }

        internal bool GeneratorFocused { get; set; }

        internal bool Focused { get; set; }
    }

    private sealed class VirtualDevice(InputDeviceDescriptor descriptor) : IInputDevice
    {
        public InputDeviceDescriptor Descriptor { get; } = descriptor;

        public IReadOnlyList<InputData> DrainEvents() => throw new InvalidOperationException("Use the ordered stream.");

        public void Dispose()
        {
        }
    }
}
