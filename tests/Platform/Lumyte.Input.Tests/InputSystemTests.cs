using System.Numerics;
using Xunit;

namespace Lumyte.Input.Tests;

/// <summary>Verifies device registration, history and lifecycle contracts.</summary>
public sealed class InputSystemTests
{
    /// <summary>Checks one-time injection enumeration and source update ordering.</summary>
    [Fact]
    public void InjectionEnumeratesOnceAndUpdatesAllSources()
    {
        int enumerations = 0;
        var order = new List<int>();
        var first = new Source { Tick = _ => order.Add(1) };
        var second = new Source { Tick = _ => order.Add(2) };
        IEnumerable<IInputSource> Sources()
        {
            enumerations++;
            yield return first;
            yield return second;
        }

        using var input = new InputSystem(Sources());
        input.Update();
        input.Update();
        Assert.Equal(1, enumerations);
        Assert.Equal([1, 2, 1, 2], order);
        Assert.Throws<ArgumentException>(() => new InputSystem([first, first]));
        Assert.Throws<ArgumentNullException>(() => new InputSystem([null!]));
    }

    /// <summary>Checks shared records and independent state across injected sources.</summary>
    [Fact]
    public void RecordsNotificationsAndPollingAgreeAcrossDevices()
    {
        var keyboard = new Device(InputDeviceKind.Keyboard);
        var mouse = new Device(InputDeviceKind.Mouse);
        var first = new Source(keyboard);
        var second = new Source(mouse);
        using var input = new InputSystem([first, second]);
        var notified = new List<InputRecord>();
        input.Recorded += notified.Add;
        keyboard.Add(new FocusData(true), new KeyData(Key.A, true, false), new KeyData(Key.A, false, false));
        mouse.Add(new FocusData(true), new MouseButtonData(MouseButton.Left, true));
        input.Update();
        Assert.NotEqual(first.Id, second.Id);
        Assert.False(input.GetState(first.Id).IsDown(Key.A));
        Assert.True(input.GetState(second.Id).IsDown(MouseButton.Left));
        InputRecord[] expected = notified.Where(record => record.DeviceId == first.Id).ToArray();
        Assert.Equal(expected, input.GetRecords(first.Id).ToArray());
        InputReadResult read = input.ReadRecords(first.Id, 0);
        Assert.Equal(expected, read.Records.ToArray());
        Assert.False(read.HasGap);
        Assert.Empty(input.ReadRecords(first.Id, read.NextSequence).Records.ToArray());
        Assert.Equal(expected, input.ReadRecords(first.Id, 0).Records.ToArray());
    }

    /// <summary>Checks retention independently of state and immutable snapshots.</summary>
    [Fact]
    public void CountRetentionPreservesStateAndReportsUnreadGaps()
    {
        var device = new Device(InputDeviceKind.Keyboard);
        var source = new Source(device);
        using var input = new InputSystem([source]);
        device.Add(new FocusData(true), new KeyData(Key.B, true, false));
        input.Update();
        ReadOnlyMemory<InputRecord> snapshot = input.GetRecords(source.Id);
        InputReadResult read = input.ReadRecords(source.Id, 0);
        input.SetRetentionPolicy(source.Id, new InputRetentionPolicy(MaxRecords: 1));
        Assert.Equal(2, input.Prune());
        Assert.True(input.ReadRecords(source.Id, 0).HasGap);
        Assert.False(input.ReadRecords(source.Id, read.NextSequence).HasGap);
        Assert.Equal(3, snapshot.Length);
        Assert.True(input.GetState(source.Id).IsDown(Key.B));
        input.ClearRecords(source.Id);
        Assert.True(input.GetState(source.Id).IsDown(Key.B));
        Assert.Throws<ArgumentOutOfRangeException>(() => input.SetRetentionPolicy(source.Id, new InputRetentionPolicy(-1)));
    }

    /// <summary>Checks time limits at the exact boundary and with no new events.</summary>
    [Fact]
    public void TimeRetentionUsesInjectedMonotonicClock()
    {
        var clock = new Clock();
        var source = new Source(new Device(InputDeviceKind.Mouse));
        using var input = new InputSystem([source], clock);
        input.Update();
        input.SetRetentionPolicy(source.Id, new InputRetentionPolicy(MaxAge: TimeSpan.FromSeconds(2)));
        clock.Advance(TimeSpan.FromSeconds(1));
        input.Update();
        Assert.Single(input.GetRecords(source.Id).ToArray());
        clock.Advance(TimeSpan.FromSeconds(1));
        input.Update();
        Assert.Empty(input.GetRecords(source.Id).ToArray());
        Assert.True(input.ReadRecords(source.Id, 0).HasGap);
    }

    /// <summary>Checks notification completion despite errors and zero-retention settings.</summary>
    [Fact]
    public void NotificationsCompleteBeforeZeroRetentionAndAggregateErrors()
    {
        var device = new Device(InputDeviceKind.Keyboard);
        var source = new Source(device);
        using var input = new InputSystem([source]);
        input.SetRetentionPolicy(source.Id, new InputRetentionPolicy(MaxRecords: 0));
        int seen = 0;
        input.Recorded += record =>
        {
            if (record.Data is FocusData)
            {
                throw new InvalidOperationException("subscriber");
            }
        };
        input.Recorded += _ =>
        {
            seen++;
            Assert.NotEmpty(input.GetRecords(source.Id).ToArray());
            Assert.Throws<InvalidOperationException>(input.Update);
            Assert.Throws<InvalidOperationException>(() => input.ClearRecords(source.Id));
        };
        device.Add(new FocusData(true));
        Assert.Throws<AggregateException>(input.Update);
        Assert.Equal(2, seen);
        Assert.Empty(input.GetRecords(source.Id).ToArray());
        input.Update();
        Assert.Equal(2, seen);
    }

    /// <summary>Checks owned-device removal without destroying borrowed sources.</summary>
    [Fact]
    public void RemovalDrainsPendingInputAndArchivesNeutralState()
    {
        var device = new Device(InputDeviceKind.Controller);
        var source = new Source(device);
        var input = new InputSystem([source]);
        device.Add(new FocusData(true), new ControllerButtonData(ControllerButton.South, true), new ControllerTriggerData(ControllerTrigger.Left, 0.8f));
        source.Tick = registry => registry.UnregisterDevice(source.Id);
        input.Update();
        Assert.Empty(input.Devices);
        InputDeviceState state = input.GetState(source.Id);
        Assert.False(state.IsConnected);
        Assert.False(state.IsDown(ControllerButton.South));
        Assert.Equal(0, state.GetTrigger(ControllerTrigger.Left));
        Assert.Contains(input.GetRecords(source.Id).ToArray(), record => record.Data is ControllerTriggerData { Value: 0 });
        Assert.Equal(1, device.Disposals);
        input.Dispose();
        input.Dispose();
        Assert.Equal(1, device.Disposals);
        Assert.Equal(1, source.Shutdowns);
        Assert.Equal(0, source.Disposals);
        Assert.Throws<ObjectDisposedException>(() => input.GetState(source.Id));
    }

    /// <summary>Checks source-scoped registry permissions and newly allocated reconnection IDs.</summary>
    [Fact]
    public void RegistriesPreventCrossSourceRemovalAndDeviceReuse()
    {
        var first = new Source(new Device(InputDeviceKind.Mouse));
        var second = new Source(new Device(InputDeviceKind.Keyboard));
        using var input = new InputSystem([first, second]);
        second.Tick = registry => Assert.Throws<InvalidOperationException>(() => registry.UnregisterDevice(first.Id));
        first.Tick = registry =>
        {
            registry.UnregisterDevice(first.Id);
            Assert.Throws<ArgumentException>(() => registry.RegisterDevice(first.Device!));
            first.Id = registry.RegisterDevice(new Device(InputDeviceKind.Mouse));
        };
        InputDeviceId old = first.Id;
        input.Update();
        Assert.NotEqual(old, first.Id);
        Assert.False(input.GetState(old).IsConnected);
        Assert.Throws<InvalidOperationException>(() => first.Registry!.RegisterDevice(new Device(InputDeviceKind.Mouse)));
    }

    /// <summary>Checks pen pressure, hovering and canceled multi-touch contacts.</summary>
    [Fact]
    public void TouchAndPenRecordsKeepPressureAndCancelOnFocusLoss()
    {
        var touch = new Device(InputDeviceKind.Touch);
        var pen = new Device(InputDeviceKind.Pen);
        var touchSource = new Source(touch);
        var penSource = new Source(pen);
        using var input = new InputSystem([touchSource, penSource]);
        touch.Add(new FocusData(true), new TouchData(new(1), TouchPhase.Began, new(2, 3), null), new TouchData(new(2), TouchPhase.Began, new(4, 5), 0));
        pen.Add(new FocusData(true), new PenData(new(1), PenPhase.Entered, Vector2.Zero, 0, false, PenButtons.None, false), new PenData(new(1), PenPhase.Down, new(1, 2), 0.7f, true, PenButtons.Barrel, true));
        input.Update();
        Assert.Equal(2, input.GetState(touchSource.Id).TouchContacts.Count);
        Assert.Null(input.GetState(touchSource.Id).TouchContacts[new(1)].Pressure);
        Assert.Equal(0.7f, input.GetState(penSource.Id).PenPointers[new(1)].Pressure);
        InputDeviceState old = input.GetState(touchSource.Id);
        touch.Add(new FocusData(false));
        pen.Add(new FocusData(false));
        input.Update();
        Assert.Empty(input.GetState(touchSource.Id).TouchContacts);
        Assert.Empty(input.GetState(penSource.Id).PenPointers);
        Assert.Equal(2, old.TouchContacts.Count);
        Assert.Equal(2, input.GetRecords(touchSource.Id).ToArray().Count(record => record.Data is TouchData { Phase: TouchPhase.Canceled }));
        Assert.Contains(input.GetRecords(penSource.Id).ToArray(), record => record.Data is PenData { Phase: PenPhase.Canceled, Pressure: 0, IsInContact: false });
    }

    /// <summary>Checks invalid batches do not expose partially updated state.</summary>
    [Fact]
    public void InvalidBatchDoesNotCommitEarlierEvents()
    {
        var device = new Device(InputDeviceKind.Pen);
        var source = new Source(device);
        using var input = new InputSystem([source]);
        device.Add(new FocusData(true));
        input.Update();
        int before = input.GetRecords(source.Id).Length;
        device.Add(new PenData(new(1), PenPhase.Entered, Vector2.Zero, 0, false, PenButtons.None, false), new PenData(new(1), PenPhase.Down, Vector2.Zero, 1.1f, true, PenButtons.None, false));
        Assert.Throws<AggregateException>(input.Update);
        Assert.Equal(before, input.GetRecords(source.Id).Length);
        Assert.Empty(input.GetState(source.Id).PenPointers);
    }

    /// <summary>Checks partial initialization rollback and borrowed ownership.</summary>
    [Fact]
    public void InitializationFailureShutsDownAndReleasesProvisionalDevices()
    {
        var first = new Source(new Device(InputDeviceKind.Keyboard));
        var second = new Source(new Device(InputDeviceKind.Mouse)) { FailInitialize = true };
        Assert.Throws<AggregateException>(() => new InputSystem([first, second]));
        Assert.Equal(1, first.Shutdowns);
        Assert.Equal(1, second.Shutdowns);
        Assert.Equal(1, first.Device!.Disposals);
        Assert.Equal(1, second.Device!.Disposals);
        Assert.Equal(0, first.Disposals);
        Assert.Throws<ObjectDisposedException>(() => first.Registry!.UnregisterDevice(first.Id));
    }

    /// <summary>Checks failed final collection still releases and neutralizes the device.</summary>
    [Fact]
    public void FinalCollectionFailureStillDisconnectsAndDisposes()
    {
        var device = new Device(InputDeviceKind.Keyboard);
        var source = new Source(device);
        using var input = new InputSystem([source]);
        device.Add(new FocusData(true), new KeyData(Key.A, true, false));
        input.Update();
        device.FailDrain = true;
        source.Tick = registry => registry.UnregisterDevice(source.Id);
        Assert.Throws<AggregateException>(input.Update);
        Assert.False(input.GetState(source.Id).IsDown(Key.A));
        Assert.False(input.GetState(source.Id).IsConnected);
        Assert.Equal(1, device.Disposals);
    }

    /// <summary>Checks source errors do not prevent other sources and devices from updating.</summary>
    [Fact]
    public void FailedSourceDoesNotBlockOtherSources()
    {
        var failing = new Source { Tick = _ => throw new InvalidOperationException("source") };
        var device = new Device(InputDeviceKind.Mouse);
        var healthy = new Source(device);
        using var input = new InputSystem([failing, healthy]);
        device.Add(new FocusData(true), new MouseMoveData(new Vector2(10, 20)));
        Assert.Throws<AggregateException>(input.Update);
        Assert.Equal(new Vector2(10, 20), input.GetState(healthy.Id).MousePosition);
        failing.Tick = null;
    }

    /// <summary>Checks repeat suppression after focus loss and non-focused input filtering.</summary>
    [Fact]
    public void FocusLossDoesNotRestoreKeysFromRepeat()
    {
        var device = new Device(InputDeviceKind.Keyboard);
        var source = new Source(device);
        using var input = new InputSystem([source]);
        device.Add(new FocusData(true), new KeyData(Key.A, true, false), new FocusData(false), new KeyData(Key.B, true, false));
        input.Update();
        Assert.False(input.GetState(source.Id).IsDown(Key.A));
        Assert.DoesNotContain(input.GetRecords(source.Id).ToArray(), record => record.Data is KeyData { Key: Key.B });
        device.Add(new FocusData(true), new KeyData(Key.A, true, true));
        input.Update();
        Assert.False(input.GetState(source.Id).IsDown(Key.A));
        device.Add(new KeyData(Key.A, true, false));
        input.Update();
        Assert.True(input.GetState(source.Id).IsDown(Key.A));
    }

    /// <summary>Checks combined count and time limits, and explicit inclusive deletion.</summary>
    [Fact]
    public void CombinedRetentionAndExplicitDeletionUseIndependentCursors()
    {
        var clock = new Clock();
        var device = new Device(InputDeviceKind.Mouse);
        var source = new Source(device);
        using var input = new InputSystem([source], clock);
        device.Add(new FocusData(true));
        input.Update();
        ulong old = input.ReadRecords(source.Id, 0).NextSequence;
        clock.Advance(TimeSpan.FromSeconds(5));
        device.Add(new MouseMoveData(new Vector2(1, 1)), new MouseWheelData(new Vector2(0, 1)));
        input.Update();
        input.SetRetentionPolicy(source.Id, new InputRetentionPolicy(1, TimeSpan.FromSeconds(1)));
        Assert.Equal(3, input.Prune());
        InputReadResult read = input.ReadRecords(source.Id, old);
        Assert.True(read.HasGap);
        Assert.Single(read.Records.ToArray());
        Assert.Equal(1, input.RemoveRecordsThrough(source.Id, read.NextSequence));
        Assert.False(input.ReadRecords(source.Id, read.NextSequence).HasGap);
        Assert.Throws<ArgumentOutOfRangeException>(() => input.ReadRecords(source.Id, read.NextSequence + 1));
    }

    /// <summary>Checks valid pen up/hover transitions and touch contact identifier reuse rejection.</summary>
    [Fact]
    public void PenHoverAndTouchEndMaintainCorrectCurrentState()
    {
        var pen = new Device(InputDeviceKind.Pen);
        var touch = new Device(InputDeviceKind.Touch);
        var penSource = new Source(pen);
        var touchSource = new Source(touch);
        using var input = new InputSystem([penSource, touchSource]);
        pen.Add(new FocusData(true), new PenData(new(1), PenPhase.Entered, Vector2.Zero, null, false, PenButtons.None, false), new PenData(new(1), PenPhase.Down, Vector2.Zero, null, true, PenButtons.None, false), new PenData(new(1), PenPhase.Up, Vector2.Zero, null, false, PenButtons.None, false));
        touch.Add(new FocusData(true), new TouchData(new(1), TouchPhase.Began, Vector2.Zero, 0.5f), new TouchData(new(1), TouchPhase.Ended, Vector2.One, 0));
        input.Update();
        Assert.False(input.GetState(penSource.Id).PenPointers[new(1)].IsInContact);
        Assert.Empty(input.GetState(touchSource.Id).TouchContacts);
        touch.Add(new TouchData(new(1), TouchPhase.Began, Vector2.Zero, null));
        Assert.Throws<AggregateException>(input.Update);
        pen.Add(new PenData(new(1), PenPhase.Left, Vector2.Zero, null, false, PenButtons.None, false));
        input.Update();
        Assert.Empty(input.GetState(penSource.Id).PenPointers);
    }

    /// <summary>Checks controller axes and the distinction between state kinds.</summary>
    [Fact]
    public void ControllerValuesAreNormalizedAndKindChecked()
    {
        var device = new Device(InputDeviceKind.Controller);
        var source = new Source(device);
        using var input = new InputSystem([source]);
        device.Add(new FocusData(true), new ControllerStickData(ControllerStick.Right, new Vector2(-1, 1)), new ControllerTriggerData(ControllerTrigger.Right, 1));
        input.Update();
        InputDeviceState state = input.GetState(source.Id);
        Assert.Equal(new Vector2(-1, 1), state.GetStick(ControllerStick.Right));
        Assert.Equal(1, state.GetTrigger(ControllerTrigger.Right));
        Assert.Throws<InvalidOperationException>(() => state.IsDown(Key.A));
        device.Add(new ControllerTriggerData(ControllerTrigger.Right, float.NaN));
        Assert.Throws<AggregateException>(input.Update);
        Assert.Equal(1, input.GetState(source.Id).GetTrigger(ControllerTrigger.Right));
    }

    private sealed class Clock : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        internal void Advance(TimeSpan duration) => _ticks += duration.Ticks;
    }

    private sealed class Device(InputDeviceKind kind) : IInputDevice
    {
        private readonly List<InputData> _pending = [];

        public InputDeviceDescriptor Descriptor { get; } = new(kind, "Test", InputDeviceIdentityKind.Physical);

        internal int Disposals { get; private set; }

        internal bool FailDrain { get; set; }

        public IReadOnlyList<InputData> DrainEvents()
        {
            if (FailDrain)
            {
                throw new InvalidOperationException("drain");
            }

            InputData[] result = _pending.ToArray();
            _pending.Clear();
            return result;
        }

        public void Dispose() => Disposals++;

        internal void Add(params InputData[] data) => _pending.AddRange(data);
    }

    private sealed class Source(Device? device = null) : IInputSource
    {
        internal Device? Device { get; } = device;

        internal IInputDeviceRegistry? Registry { get; private set; }

        internal InputDeviceId Id { get; set; }

        internal Action<IInputDeviceRegistry>? Tick { get; set; }

        internal bool FailInitialize { get; init; }

        internal int Shutdowns { get; private set; }

        internal int Disposals { get; private set; }

        public void Initialize(IInputDeviceRegistry registry)
        {
            Registry = registry;
            if (Device is not null)
            {
                Id = registry.RegisterDevice(Device);
            }

            if (FailInitialize)
            {
                throw new InvalidOperationException("initialize");
            }
        }

        public void Update() => Tick?.Invoke(Registry!);

        public void Shutdown() => Shutdowns++;

        public void Dispose() => Disposals++;
    }
}
