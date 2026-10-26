using Lumyte.Audio.Abstractions;
using Lumyte.Audio.Execution;

namespace Lumyte.Resources.Audio.IntegrationTests;

internal sealed class TestFactory : IAudioDeviceFactory
{
    private readonly object _gate = new();
    private readonly List<TestDevice> _devices = [];
    private int _failures;
    private PendingDeviceCreation? _pending;

    internal TestDevice Current
    {
        get
        {
            lock (_gate)
            {
                return _devices[^1];
            }
        }
    }

    internal int CreationCount
    {
        get
        {
            lock (_gate)
            {
                return _devices.Count;
            }
        }
    }

    public Task<IAudioDevice> CreateAsync()
    {
        lock (_gate)
        {
            if (_failures > 0)
            {
                _failures--;
                return Task.FromException<IAudioDevice>(new InvalidOperationException("Injected device creation failure."));
            }

            var device = new TestDevice();
            _devices.Add(device);
            if (_pending is { } pending)
            {
                _pending = null;
                pending.Attach(device);
                return pending.Completion.Task;
            }

            return Task.FromResult<IAudioDevice>(device);
        }
    }

    internal void FailNextCreation()
    {
        lock (_gate)
        {
            _failures++;
        }
    }

    internal PendingDeviceCreation DelayNextCreation()
    {
        lock (_gate)
        {
            return _pending = new();
        }
    }
}
