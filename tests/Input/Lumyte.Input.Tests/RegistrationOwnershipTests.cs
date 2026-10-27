using Lumyte.Input.Abstractions;
using Xunit;

namespace Lumyte.Input.Tests;

/// <summary>Verifies ownership remains with the source when registration observers fail.</summary>
public sealed class RegistrationOwnershipTests
{
    /// <summary>Checks constructor rollback does not dispose a device whose registration failed.</summary>
    [Fact]
    public void ObserverFailureDuringInitializationLeavesDisposalToSource()
    {
        var device = new Device();
        var source = new Source { InitializeDevice = device };
        var inspection = new Inspection { FailRegistration = true };

        Assert.Throws<AggregateException>(() => new InputSystem([source], inspection));

        Assert.Equal(1, device.Disposals);
        Assert.Equal(0, device.Drains);
        Assert.Equal(1, source.Shutdowns);
    }

    /// <summary>Checks failed runtime registration leaves no active device and does not reuse its identifier.</summary>
    [Fact]
    public void ObserverFailureDuringUpdateRemovesProvisionalRegistration()
    {
        var source = new Source();
        var inspection = new Inspection { FailRegistration = true };
        var rejected = new Device();
        using var input = new InputSystem([source], inspection);
        source.PendingDevice = rejected;

        Assert.Throws<AggregateException>(input.Update);

        Assert.Empty(input.Devices);
        Assert.Empty(input.DeviceInfos);
        Assert.Equal(1, rejected.Disposals);
        Assert.Equal(0, rejected.Drains);
        InputDeviceId rejectedId = inspection.LastId;
        inspection.FailRegistration = false;
        var accepted = new Device();
        source.PendingDevice = accepted;
        input.Update();
        Assert.NotEqual(rejectedId, inspection.LastId);
        Assert.Same(accepted, Assert.Single(input.Devices).Value);
        Assert.Equal(1, accepted.Drains);

        input.Dispose();
        Assert.Equal(1, rejected.Disposals);
        Assert.Equal(1, accepted.Disposals);
    }

    private sealed class Device : IInputDevice
    {
        public InputDeviceDescriptor Descriptor { get; } = new(InputDeviceKind.Keyboard, "Test", InputDeviceIdentityKind.Logical);

        internal int Disposals { get; private set; }

        internal int Drains { get; private set; }

        public IReadOnlyList<InputData> DrainEvents()
        {
            Drains++;
            return [];
        }

        public void Dispose() => Disposals++;
    }

    private sealed class Source : IInputSource
    {
        private IInputDeviceRegistry? _registry;

        internal Device? InitializeDevice { get; init; }

        internal Device? PendingDevice { get; set; }

        internal int Shutdowns { get; private set; }

        public void Initialize(IInputDeviceRegistry registry)
        {
            _registry = registry;
            if (InitializeDevice is { } device)
            {
                Register(device);
            }
        }

        public void Update()
        {
            if (PendingDevice is { } device)
            {
                PendingDevice = null;
                Register(device);
            }
        }

        public void Shutdown() => Shutdowns++;

        public void Dispose()
        {
        }

        private void Register(Device device)
        {
            try
            {
                _registry!.RegisterDevice(device);
            }
            catch
            {
                device.Dispose();
                throw;
            }
        }
    }

    private sealed class Inspection : IInputInspection
    {
        internal bool FailRegistration { get; set; }

        internal InputDeviceId LastId { get; private set; }

        public void SystemCreated(InputSystem system)
        {
        }

        public void SourceInitializing(InputSystem system, IInputSource source)
        {
        }

        public void SourceUpdated(InputSystem system, IInputSource source)
        {
        }

        public void SourceShutdown(InputSystem system, IInputSource source)
        {
        }

        public void Registered(InputSystem system, InputDeviceId device, InputDeviceDescriptor descriptor)
        {
            LastId = device;
            Assert.Contains(device, system.Devices.Keys);
            if (FailRegistration)
            {
                throw new InvalidOperationException("Registration observer failed.");
            }
        }

        public void DeviceUpdated(InputSystem system, InputDeviceId device, InputDeviceState state, ulong latestSequence)
        {
        }

        public void Unregistered(InputSystem system, InputDeviceId device)
        {
        }

        public void SystemDisposed(InputSystem system)
        {
        }
    }
}
