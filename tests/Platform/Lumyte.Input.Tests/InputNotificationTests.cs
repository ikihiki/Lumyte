using Xunit;

namespace Lumyte.Input.Tests;

/// <summary>Verifies per-record subscription snapshots and notification error isolation.</summary>
public sealed class InputNotificationTests
{
    /// <summary>Checks subscription changes take effect on the next record in the same update.</summary>
    [Fact]
    public void SubscriptionChangesApplyStartingWithTheNextRecord()
    {
        var source = new KeyboardSource();
        using var input = new InputSystem([source]);
        input.Update();
        var calls = new List<(string Handler, bool IsDown)>();
        Action<InputRecord> removed = record =>
        {
            if (record.Data is KeyData key)
            {
                calls.Add(("removed", key.IsDown));
            }
        };
        Action<InputRecord> added = record =>
        {
            if (record.Data is KeyData key)
            {
                calls.Add(("added", key.IsDown));
            }
        };
        input.Recorded += record =>
        {
            if (record.Data is KeyData key)
            {
                calls.Add(("first", key.IsDown));
                if (key.IsDown)
                {
                    input.Recorded -= removed;
                    input.Recorded += added;
                }
            }
        };
        input.Recorded += removed;
        source.Enqueue(new KeyData(Key.A, true, false), new KeyData(Key.A, false, false));

        input.Update();

        Assert.Equal(new[] { ("first", true), ("removed", true), ("first", false), ("added", false) }, calls);
    }

    /// <summary>Checks failing handlers do not skip other handlers or later records.</summary>
    [Fact]
    public void HandlerFailuresPreserveRemainingNotificationsAndAreAggregated()
    {
        var source = new KeyboardSource();
        using var input = new InputSystem([source]);
        input.Update();
        var calls = new List<(string Handler, bool IsDown)>();
        input.Recorded += record =>
        {
            if (record.Data is KeyData key)
            {
                calls.Add(("failing", key.IsDown));
                throw new InvalidOperationException(key.IsDown ? "press" : "release");
            }
        };
        input.Recorded += record =>
        {
            if (record.Data is KeyData key)
            {
                calls.Add(("remaining", key.IsDown));
            }
        };
        source.Enqueue(new KeyData(Key.A, true, false), new KeyData(Key.A, false, false));

        AggregateException error = Assert.Throws<AggregateException>(input.Update);

        Assert.Equal(new[] { ("failing", true), ("remaining", true), ("failing", false), ("remaining", false) }, calls);
        Assert.Equal(new[] { "press", "release" }, error.InnerExceptions.Select(exception => exception.Message));
        input.Update();
        Assert.Equal(4, calls.Count);
    }

    private sealed class KeyboardSource : IInputSource
    {
        private readonly KeyboardDevice _device = new();

        public void Enqueue(params InputData[] data) => _device.Enqueue(data);

        public void Initialize(IInputDeviceRegistry registry)
        {
            registry.RegisterDevice(_device);
            _device.Enqueue(new FocusData(true));
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
    }

    private sealed class KeyboardDevice : IInputDevice
    {
        private readonly List<InputData> _pending = new();

        public InputDeviceDescriptor Descriptor { get; } = new(InputDeviceKind.Keyboard, "notification test keyboard", InputDeviceIdentityKind.Physical);

        public void Enqueue(params InputData[] data) => _pending.AddRange(data);

        public IReadOnlyList<InputData> DrainEvents()
        {
            InputData[] data = _pending.ToArray();
            _pending.Clear();
            return data;
        }

        public void Dispose()
        {
        }
    }
}
