using Lumyte.Input.Abstractions;

namespace Lumyte.Platform.Internal;

internal sealed class BufferedInputSource(Action check, Action<BufferedInputSource> release, string platformName) : IInputSource
{
    private Device? _keyboard;
    private Device? _mouse;
    private bool _active;
    private bool _disposed;

    public void Initialize(IInputDeviceRegistry registry)
    {
        Check();
        ArgumentNullException.ThrowIfNull(registry);
        if (_active)
        {
            throw new InvalidOperationException("The source is already initialized.");
        }

        var keyboard = new Device(check, new InputDeviceDescriptor(InputDeviceKind.Keyboard, platformName + " keyboard", InputDeviceIdentityKind.Logical));
        var mouse = new Device(check, new InputDeviceDescriptor(InputDeviceKind.Mouse, platformName + " mouse", InputDeviceIdentityKind.Logical));
        InputDeviceId id;
        try
        {
            id = registry.RegisterDevice(keyboard);
        }
        catch
        {
            keyboard.Dispose();
            mouse.Dispose();
            throw;
        }

        try
        {
            registry.RegisterDevice(mouse);
        }
        catch
        {
            mouse.Dispose();
            registry.UnregisterDevice(id);
            throw;
        }

        _keyboard = keyboard;
        _mouse = mouse;
        _active = true;
    }

    public void Update() => Check();

    public void Shutdown()
    {
        Check();
        _active = false;
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
        release(this);
    }

    internal void Keyboard(InputData data)
    {
        if (_active)
        {
            _keyboard!.Add(data);
        }
    }

    internal void Mouse(InputData data)
    {
        if (_active)
        {
            _mouse!.Add(data);
        }
    }

    internal void Focus(bool focused)
    {
        var data = new FocusData(focused);
        Keyboard(data);
        Mouse(data);
    }

    private void Check()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        check();
    }

    private sealed class Device(Action check, InputDeviceDescriptor descriptor) : IInputDevice
    {
        private readonly List<InputData> _events = [];
        private bool _disposed;

        public InputDeviceDescriptor Descriptor { get; } = descriptor;

        public IReadOnlyList<InputData> DrainEvents()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            check();
            IReadOnlyList<InputData> batch = Array.AsReadOnly(_events.ToArray());
            _events.Clear();
            return batch;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                check();
                _events.Clear();
                _disposed = true;
            }
        }

        internal void Add(InputData data)
        {
            if (!_disposed)
            {
                _events.Add(data);
            }
        }
    }
}
