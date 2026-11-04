using Lumyte.Input.Abstractions;
using Lumyte.Platform.Abstractions;
using Lumyte.Windowing.Abstractions;

namespace Lumyte.Platform.Internal;

internal sealed class BufferedInputSource(Action check, Action<BufferedInputSource> release, Func<IEnumerable<IWindow>> windows, string platformName, Action? stopAcquisition = null, Action? startAcquisition = null) : IWindowInputSource
{
    private readonly Dictionary<WindowId, InputWindowId> _windows = [];
    private readonly HashSet<WindowId> _closed = [];
    private readonly Queue<InputEventBatch> _batches = [];
    private readonly List<InputSourceEvent> _pending = [];
    private InputDeviceId _keyboard;
    private InputDeviceId _mouse;
    private ulong _capture;
    private ulong _batch;
    private ulong _committed;
    private int _count;
    private bool _active;
    private bool _aborted;
    private bool _disposed;
    private Exception? _fault;

    public Guid StreamId { get; } = Guid.NewGuid();

    public void Initialize(IInputDeviceRegistry registry)
    {
        Check();
        ArgumentNullException.ThrowIfNull(registry);
        if (_active || _aborted || _keyboard.Value != 0)
        {
            throw new InvalidOperationException("This connection cannot be initialized again.");
        }

        _keyboard = registry.RegisterDevice(new Device(new(InputDeviceKind.Keyboard, platformName + " keyboard", InputDeviceIdentityKind.Logical)));
        try
        {
            _mouse = registry.RegisterDevice(new Device(new(InputDeviceKind.Mouse, platformName + " mouse", InputDeviceIdentityKind.Logical)));
        }
        catch
        {
            registry.UnregisterDevice(_keyboard);
            throw;
        }

        startAcquisition?.Invoke();
        _active = true;
        foreach (IWindow window in windows())
        {
            Focus(window.Id, window.State.IsActive);
        }
    }

    public void Update()
    {
        Check();
        if (_fault is not null)
        {
            throw new InvalidOperationException("Input acquisition stopped; stop this source explicitly.", _fault);
        }

        if (_active && _pending.Count == 0 && _batches.Count == 0)
        {
            _batches.Enqueue(new(checked(++_batch), _capture, Array.Empty<InputSourceEvent>()));
        }
    }

    public bool TryGetInputWindowId(WindowId windowId, out InputWindowId inputWindowId)
    {
        Check();
        if (_aborted || _closed.Contains(windowId) || !windows().Any(window => window.Id == windowId && window.State.Lifetime == WindowLifetime.Open))
        {
            inputWindowId = default;
            return false;
        }

        if (!_windows.TryGetValue(windowId, out inputWindowId))
        {
            inputWindowId = new(Guid.NewGuid());
            _windows.Add(windowId, inputWindowId);
        }

        return true;
    }

    public InputEventBatch? PeekEvents()
    {
        Check();
        Seal();
        return _batches.TryPeek(out InputEventBatch? batch) ? batch : null;
    }

    public void CommitEvents(ulong batchId)
    {
        Check();
        if (batchId == _committed && batchId != 0)
        {
            return;
        }

        if (!_batches.TryPeek(out InputEventBatch? batch) || batch.Id != batchId)
        {
            throw new ArgumentException("Only the first batch can be acknowledged.", nameof(batchId));
        }

        _count -= batch.Events.Count;
        _batches.Dequeue();
        _committed = batchId;
    }

    public void AbortEvents()
    {
        Check();
        if (_aborted)
        {
            return;
        }

        try
        {
            stopAcquisition?.Invoke();
        }
        finally
        {
            _aborted = true;
            _active = false;
            _pending.Clear();
            _batches.Clear();
            _windows.Clear();
            _count = 0;
        }
    }

    public void Shutdown()
    {
        Check();
        try
        {
            if (_active)
            {
                stopAcquisition?.Invoke();
            }
        }
        finally
        {
            _active = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Check();
        if (_active)
        {
            throw new InvalidOperationException("Shut down the consumer before disposing the source.");
        }

        _disposed = true;
        _pending.Clear();
        _batches.Clear();
        _windows.Clear();
        release(this);
    }

    internal void FailAcquisition(Exception error) => _fault ??= error;

    internal ulong BeginAcquisition() => checked(++_capture);

    internal void Keyboard(WindowId window, InputData data, ulong? capture = null) => Add(_keyboard, window, data, capture ?? BeginAcquisition());

    internal void Mouse(WindowId window, InputData data, ulong? capture = null) => Add(_mouse, window, data, capture ?? BeginAcquisition());

    internal void Focus(WindowId window, bool focused)
    {
        ulong capture = BeginAcquisition();
        Keyboard(window, new FocusData(focused), capture);
        Mouse(window, new FocusData(focused), capture);
    }

    internal void Close(WindowId window)
    {
        if (_windows.TryGetValue(window, out InputWindowId id) && _active && !_aborted)
        {
            ulong capture = BeginAcquisition();
            Enqueue(new(_keyboard, id, capture, new WindowInputClosedData()));
            Enqueue(new(_mouse, id, capture, new WindowInputClosedData()));
        }

        _windows.Remove(window);
        _closed.Add(window);
    }

    private void Add(InputDeviceId device, WindowId window, InputData data, ulong capture)
    {
        if (_active && !_aborted && _fault is null && TryGetInputWindowId(window, out InputWindowId id))
        {
            Enqueue(new(device, id, capture, data));
        }
    }

    private void Enqueue(InputSourceEvent value)
    {
        if (_count == 16384)
        {
            _fault = new InvalidOperationException("Unacknowledged acquisition exceeded 16384 events.");
            throw _fault;
        }

        if (_pending.Count == 4096)
        {
            Seal();
        }

        _pending.Add(value);
        _count++;
    }

    private void Seal()
    {
        if (_pending.Count != 0)
        {
            _batches.Enqueue(new(checked(++_batch), _pending[^1].CaptureSequence ?? _capture, _pending));
            _pending.Clear();
        }
    }

    private void Check()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        check();
    }

    private sealed class Device(InputDeviceDescriptor descriptor) : IInputDevice
    {
        public InputDeviceDescriptor Descriptor { get; } = descriptor;

        public IReadOnlyList<InputData> DrainEvents() => throw new InvalidOperationException("Use the ordered source, not device queues.");

        public void Dispose()
        {
        }
    }
}
