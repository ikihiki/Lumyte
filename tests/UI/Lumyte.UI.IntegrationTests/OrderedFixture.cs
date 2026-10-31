using Lumyte.Input.Abstractions;
using Lumyte.Platform.Abstractions;
using Lumyte.Windowing.Abstractions;

namespace Lumyte.UI.IntegrationTests;

internal sealed class OrderedFixture(params InputDeviceKind[] kinds) : IWindowInputSource
{
    private readonly Queue<InputEventBatch> _events = [];
    private ulong _id;
    private ulong _cause;
    private ulong _committed;

    public Guid StreamId { get; } = Guid.NewGuid();

    internal List<InputDeviceId> Devices { get; } = [];

    internal Dictionary<WindowId, InputWindowId> Windows { get; } = [];

    internal int Acks { get; private set; }

    internal bool Aborted { get; private set; }

    public void Initialize(IInputDeviceRegistry registry)
    {
        foreach (InputDeviceKind kind in kinds)
        {
            Devices.Add(registry.RegisterDevice(new Device(kind)));
        }
    }

    public void Update()
    {
    }

    public void Shutdown()
    {
    }

    public void Dispose()
    {
    }

    public InputEventBatch? PeekEvents() => _events.TryPeek(out InputEventBatch? value) ? value : null;

    public void CommitEvents(ulong batchId)
    {
        if (batchId == _committed && batchId != 0)
        {
            return;
        }

        if (_events.Peek().Id != batchId)
        {
            throw new ArgumentException("Out-of-order acknowledgment.", nameof(batchId));
        }

        _events.Dequeue();
        _committed = batchId;
        Acks++;
    }

    public void AbortEvents()
    {
        Aborted = true;
        _events.Clear();
    }

    public bool TryGetInputWindowId(WindowId windowId, out InputWindowId inputWindowId) => Windows.TryGetValue(windowId, out inputWindowId);

    internal void Add(params (int Device, InputWindowId? Window, InputData Data)[] events) => _events.Enqueue(new(++_id, _cause + (ulong)events.Length, events.Select(item => new InputSourceEvent(Devices[item.Device], item.Window, ++_cause, item.Data)).ToArray()));

    private sealed class Device(InputDeviceKind kind) : IInputDevice
    {
        public InputDeviceDescriptor Descriptor { get; } = new(kind, "fixture", InputDeviceIdentityKind.Logical);

        public IReadOnlyList<InputData> DrainEvents() => throw new InvalidOperationException("The device queue must never be used.");

        public void Dispose()
        {
        }
    }
}
