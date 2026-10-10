using System.Numerics;
using Lumyte.Input.Actions;
using Lumyte.Input.Processing;
using Lumyte.Input.Settings;
using Xunit;

namespace Lumyte.Input.Advanced.Tests;

/// <summary>Verifies correction, mapping and operation lifecycle behavior.</summary>
public sealed class AdvancedInputTests
{
    /// <summary>Verifies radial scaling and unavailable pressure preservation.</summary>
    [Fact]
    public void CorrectionScalesRadiallyAndPreservesMissingPressure()
    {
        var correction = new AnalogCorrection(new Vector2(0.1f, 0), 0.2f, 2);
        IReadOnlyList<InputData> data = correction.Process(new InputData[] { new ControllerStickData(ControllerStick.Left, new Vector2(0.2f, 0)), new ControllerStickData(ControllerStick.Left, new Vector2(0.7f, 0)), new TouchData(new TouchContactId(1), TouchPhase.Began, Vector2.Zero, null), new TouchData(new TouchContactId(2), TouchPhase.Began, Vector2.Zero, 0.5f), }, TimeSpan.Zero);
        Assert.Equal(Vector2.Zero, Assert.IsType<ControllerStickData>(data[0]).Value);
        Assert.Equal(0.5f, Assert.IsType<ControllerStickData>(data[1]).Value.X, 5);
        Assert.Null(Assert.IsType<TouchData>(data[2]).Pressure);
        Assert.Equal(0.25f, Assert.IsType<TouchData>(data[3]).Pressure);
    }

    /// <summary>Verifies invalid correction values are rejected.</summary>
    /// <param name = "deadZone">The invalid dead zone.</param>
    [Theory]
    [InlineData(-0.1f)]
    [InlineData(1f)]
    [InlineData(float.NaN)]
    public void InvalidDeadZoneIsRejected(float deadZone) => Assert.Throws<ArgumentOutOfRangeException>(() => new AnalogCorrection(Vector2.Zero, deadZone));

    /// <summary>Verifies correction is recorded by InputSystem.</summary>
    [Fact]
    public void InjectedSourceRecordsCorrectedDataAndOwnsDevicesOnce()
    {
        var device = new TestDevice(InputDeviceKind.Controller);
        var backend = new TestSource(device);
        var source = new CorrectingInputSource(backend, _ => new[] { new AnalogCorrection(Vector2.Zero, 0.2f) }, () => TimeSpan.Zero);
        using (var input = new InputSystem(new[] { source }))
        {
            device.Enqueue(new ControllerStickData(ControllerStick.Left, new Vector2(0.1f, 0)));
            input.Update();
            Assert.Equal(Vector2.Zero, input.GetState(backend.Id).GetStick(ControllerStick.Left));
            Assert.Contains(input.GetRecords(backend.Id).ToArray(), record => record.Data is ControllerStickData { Value: var value } && value == Vector2.Zero);
        }

        source.Dispose();
        Assert.Equal(1, device.DisposeCount);
        Assert.Equal(0, backend.DisposeCount);
    }

    /// <summary>Verifies failed wrapping does not dispose the source-owned device.</summary>
    [Fact]
    public void FailedRegistrationPreservesCallerOwnership()
    {
        var device = new TestDevice(InputDeviceKind.Controller);
        var source = new CorrectingInputSource(new TestSource(device), _ => [], () => TimeSpan.Zero);
        Assert.Throws<InvalidOperationException>(() => source.Initialize(new RejectingRegistry()));
        Assert.Equal(0, device.DisposeCount);
    }

    /// <summary>Verifies physical and virtual input use one acquisition in the same update.</summary>
    [Fact]
    public void TouchVirtualDeviceUsesSameUpdateAndCanceledSwipeDoesNotFire()
    {
        var device = new TestDevice(InputDeviceKind.Touch);
        var source = new VirtualizingInputSource(new TestSource(device), _ => new TouchControllerGenerator(), () => TimeSpan.Zero);
        using var input = new InputSystem(new[] { source });
        device.Enqueue(new TouchData(new TouchContactId(1), TouchPhase.Began, Vector2.Zero, null));
        device.Enqueue(new TouchData(new TouchContactId(1), TouchPhase.Moved, new Vector2(100, 0), null));
        input.Update();
        InputDeviceId virtualId = input.DeviceInfos.Single(info => info.IdentityKind == InputDeviceIdentityKind.Logical).Id;
        Assert.Equal(Vector2.UnitX, input.GetState(virtualId).GetStick(ControllerStick.Left));
        Assert.Equal(1, device.DrainCount);
        device.Enqueue(new TouchData(new TouchContactId(1), TouchPhase.Canceled, new Vector2(100, 0), null));
        input.Update();
        Assert.Equal(Vector2.Zero, input.GetState(virtualId).GetStick(ControllerStick.Left));
        Assert.DoesNotContain(input.GetRecords(virtualId).ToArray(), record => record.Data is ControllerButtonData { IsDown: true });
    }

    /// <summary>Verifies swipe, pinch and rotation controller outputs.</summary>
    [Fact]
    public void TouchRecognizesSwipePinchAndRotation()
    {
        var generator = new TouchControllerGenerator();
        InputData[] data = [new FocusData(true), new TouchData(new TouchContactId(1), TouchPhase.Began, Vector2.Zero, null), new TouchData(new TouchContactId(2), TouchPhase.Began, new Vector2(10, 0), null), new TouchData(new TouchContactId(2), TouchPhase.Moved, new Vector2(0, 110), null), new TouchData(new TouchContactId(2), TouchPhase.Ended, new Vector2(0, 110), null),];
        IReadOnlyList<InputData> output = generator.Generate(new InputDeviceId(1), data, TimeSpan.Zero);
        Assert.Contains(output, item => item is ControllerStickData { Stick: ControllerStick.Right, Value: var value } && value.X == 1 && Math.Abs(value.Y - 0.5f) < 0.001);
        Assert.Contains(output, item => item is ControllerButtonData { IsDown: true });
    }

    /// <summary>Verifies live correction cancels and filters old touch contacts.</summary>
    [Fact]
    public void CorrectionChangeCancelsExistingTouches()
    {
        var device = new TestDevice(InputDeviceKind.Touch);
        var backend = new TestSource(device);
        var source = new CorrectingInputSource(backend, _ => [], () => TimeSpan.Zero);
        using var input = new InputSystem(new[] { source });
        device.Enqueue(new TouchData(new TouchContactId(1), TouchPhase.Began, Vector2.Zero, null));
        input.Update();
        source.SetProcessorFactory(_ => new[] { new AnalogCorrection(Vector2.Zero) });
        device.Enqueue(new TouchData(new TouchContactId(1), TouchPhase.Moved, Vector2.One, null));
        input.Update();
        Assert.Empty(input.GetState(backend.Id).TouchContacts);
        device.Enqueue(new TouchData(new TouchContactId(1), TouchPhase.Ended, Vector2.One, null));
        device.Enqueue(new TouchData(new TouchContactId(2), TouchPhase.Began, Vector2.One, 0.5f));
        input.Update();
        Assert.Single(input.GetState(backend.Id).TouchContacts);
    }

    /// <summary>Verifies press buffers survive ordinary release and consume once.</summary>
    [Fact]
    public void ReleasedPressRemainsBufferedAndConsumesOnce()
    {
        ActionSystem actions = Create();
        actions.Advance(new[] { Record(1, 0, true), Record(2, 10, false) }, At(10));
        Assert.Equal(Vector2.Zero, actions.GetState("jump").Value);
        Assert.True(actions.Buffer.TryConsume("press", At(10), out _));
        Assert.False(actions.Buffer.TryConsume("press", At(10), out _));
    }

    /// <summary>Verifies the exact expiry boundary and bounded capacity.</summary>
    [Fact]
    public void BufferExpiresAtBoundaryAndEvictsOldest()
    {
        var buffer = new ActionInputBuffer(new InputBufferOptions(At(100), 2));
        for (int i = 0; i < 3; i++)
        {
            buffer.Add(new RecognizedAction(i.ToString(), "game", At(i), Vector2.One, []), At(i));
        }

        Assert.False(buffer.TryConsume("0", At(3), out _));
        Assert.False(buffer.TryConsume("1", At(101), out _));
        Assert.True(buffer.TryConsume("2", At(101), out _));
    }

    /// <summary>Verifies operation timers advance on empty input.</summary>
    [Fact]
    public void HoldRunsWithoutNewRecordsAndFiresOnce()
    {
        ActionProfile profile = Profile() with
        {
            Recognitions = [new RecognitionDefinition("hold", "game", RecognitionKind.Hold, ["jump"], At(50))],
        };
        var actions = new ActionSystem(profile);
        actions.ActivateContext("game");
        actions.Advance(new[] { Record(1, 0, true) }, At(0));
        actions.Advance(ReadOnlyMemory<InputRecord>.Empty, At(60));
        Assert.True(actions.Buffer.TryConsume("hold", At(60), out RecognizedAction? held));
        Assert.Equal(At(50), held!.At);
        actions.Advance(ReadOnlyMemory<InputRecord>.Empty, At(70));
        Assert.False(actions.Buffer.TryConsume("hold", At(70), out _));
    }

    /// <summary>Verifies double taps require separate starts.</summary>
    [Fact]
    public void DoubleTapRecognizesSeparatePresses()
    {
        var actions = new ActionSystem(Profile() with { Recognitions = [new RecognitionDefinition("double", "game", RecognitionKind.MultiTap, ["jump"], At(100))] });
        actions.ActivateContext("game");
        actions.Advance(new[] { Record(1, 0, true), Record(2, 10, false), Record(3, 20, true) }, At(20));
        Assert.True(actions.Buffer.TryConsume("double", At(20), out _));
    }

    /// <summary>Verifies focus loss clears states and completed input buffers.</summary>
    [Fact]
    public void FocusLossCancelsAndClearsBuffer()
    {
        ActionSystem actions = Create();
        actions.Advance(new[] { Record(1, 0, true), new InputRecord(new InputDeviceId(1), 2, At(10), new FocusData(false)) }, At(10));
        Assert.Equal(Vector2.Zero, actions.GetState("jump").Value);
        Assert.Equal(0, actions.Buffer.Count);
    }

    /// <summary>Verifies newly activated exclusive contexts do not inherit held buttons.</summary>
    [Fact]
    public void ContextSwitchSuppressesHeldButtonsUntilRelease()
    {
        ActionProfile profile = Profile() with
        {
            Contexts = [new InputContext("game"), new InputContext("menu", 10, true)],
            Bindings = [new ActionBinding("jump-key", "jump", "game", InputControl.ForKey(Key.Space), Vector2.UnitX), new ActionBinding("menu-key", "jump", "menu", InputControl.ForKey(Key.Space), Vector2.UnitX)],
        };
        var actions = new ActionSystem(profile);
        actions.ActivateContext("game");
        actions.Advance(new[] { Record(1, 0, true) }, At(0));
        actions.ActivateContext("menu");
        actions.Advance(ReadOnlyMemory<InputRecord>.Empty, At(1));
        Assert.Equal(Vector2.Zero, actions.GetState("jump").Value);
        actions.Advance(new[] { Record(2, 2, false), Record(3, 3, true) }, At(3));
        Assert.Equal(Vector2.UnitX, actions.GetState("jump").Value);
    }

    /// <summary>Verifies rebind candidates remain separate from the running profile.</summary>
    [Fact]
    public void RebindPreparesWithoutChangingProfile()
    {
        ActionSystem actions = Create();
        RebindSession session = actions.BeginRebind("jump-key", new RebindOptions(At(100)));
        actions.Advance(new[] { Record(1, 0, true, Key.A) }, At(0));
        Assert.Equal(InputControl.ForKey(Key.A), session.Candidate);
        ActionProfile candidate = session.PrepareProfile(RebindConflictPolicy.Reject);
        Assert.Equal(InputControl.ForKey(Key.A), candidate.Bindings[0].Control);
        Assert.Equal(InputControl.ForKey(Key.Space), actions.ExportProfile().Bindings[0].Control);
        session.Confirm(RebindConflictPolicy.Reject);
        actions.Advance(ReadOnlyMemory<InputRecord>.Empty, At(1));
        Assert.Equal(InputControl.ForKey(Key.A), actions.ExportProfile().Bindings[0].Control);
        Assert.Equal(Vector2.Zero, actions.GetState("jump").Value);
    }

    /// <summary>Verifies initial holds and repeats cannot be captured.</summary>
    [Fact]
    public void RebindExcludesHeldAndRepeatedInputAndTimesOut()
    {
        ActionSystem actions = Create();
        actions.Advance(new[] { Record(1, 0, true) }, At(0));
        RebindSession session = actions.BeginRebind("jump-key", new RebindOptions(At(100)));
        actions.Advance(new[] { new InputRecord(new InputDeviceId(1), 2, At(1), new KeyData(Key.Space, true, true)) }, At(1));
        Assert.Null(session.Candidate);
        actions.Advance(ReadOnlyMemory<InputRecord>.Empty, At(100));
        Assert.True(session.IsComplete);
    }

    /// <summary>Verifies stale records cannot be replayed.</summary>
    [Fact]
    public void RejectsReplayAndFutureRecords()
    {
        ActionSystem actions = Create();
        actions.Advance(new[] { Record(1, 0, true) }, At(0));
        Assert.Throws<ArgumentException>(() => actions.Advance(new[] { Record(1, 1, false) }, At(1)));
        Assert.Throws<ArgumentException>(() => actions.Advance(new[] { Record(2, 10, false) }, At(1)));
    }

    /// <summary>Verifies notification errors do not prevent other handlers or replay state.</summary>
    [Fact]
    public void CallbackFailuresAreAggregatedAfterStateCommit()
    {
        ActionSystem actions = Create();
        int called = 0;
        actions.Changed += _ => throw new InvalidOperationException();
        actions.Changed += _ => called++;
        Assert.Throws<AggregateException>(() => actions.Advance(new[] { Record(1, 0, true) }, At(0)));
        Assert.Equal(1, called);
        Assert.Equal(Vector2.UnitX, actions.GetState("jump").Value);
        Assert.Throws<ArgumentException>(() => actions.Advance(new[] { Record(1, 0, true) }, At(0)));
    }

    /// <summary>Verifies persisted profiles use named controls and round-trip immutably.</summary>
    [Fact]
    public void ProfileSettingsRoundTripUsesStableTokens()
    {
        InputActionSettings settings = InputSettingsConverter.ToSettings(Profile());
        Assert.Equal("Space", settings.Bindings[0].Control);
        ActionProfile profile = InputSettingsConverter.BuildProfile(settings);
        settings.Bindings[0].Control = "A";
        Assert.Equal(InputControl.ForKey(Key.Space), profile.Bindings[0].Control);
        Assert.False(InputSettingsConverter.ValidateActions(new InputActionSettings { Bindings = [new BindingSettings { Control = "42" }] }));
    }

    private static ActionSystem Create()
    {
        var actions = new ActionSystem(Profile());
        actions.ActivateContext("game");
        return actions;
    }

    private static ActionProfile Profile() => new([new ActionDefinition("jump", ActionValueKind.Button)], [new ActionBinding("jump-key", "jump", "game", InputControl.ForKey(Key.Space), Vector2.UnitX)], [new InputContext("game")], [new RecognitionDefinition("press", "game", RecognitionKind.Press, ["jump"], At(100))]);

    private static InputRecord Record(ulong sequence, int milliseconds, bool down, Key key = Key.Space) => new(new InputDeviceId(1), sequence, At(milliseconds), new KeyData(key, down, false));

    private static TimeSpan At(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

    private sealed class TestDevice(InputDeviceKind kind) : IInputDevice
    {
        private readonly List<InputData> _queue = new();

        public InputDeviceDescriptor Descriptor { get; } = new(kind, "test", InputDeviceIdentityKind.Physical);

        public int DisposeCount { get; private set; }

        public int DrainCount { get; private set; }

        public void Enqueue(InputData data) => _queue.Add(data);

        public IReadOnlyList<InputData> DrainEvents()
        {
            DrainCount++;
            InputData[] result = _queue.ToArray();
            _queue.Clear();
            return result;
        }

        public void Dispose() => DisposeCount++;
    }

    private sealed class TestSource(TestDevice device) : IInputSource
    {
        public InputDeviceId Id { get; private set; }

        public int DisposeCount { get; private set; }

        public void Initialize(IInputDeviceRegistry registry)
        {
            Id = registry.RegisterDevice(device);
            device.Enqueue(new FocusData(true));
        }

        public void Update()
        {
        }

        public void Shutdown()
        {
        }

        public void Dispose() => DisposeCount++;
    }

    private sealed class RejectingRegistry : IInputDeviceRegistry
    {
        public InputDeviceId RegisterDevice(IInputDevice device) => throw new InvalidOperationException();

        public void UnregisterDevice(InputDeviceId device)
        {
        }
    }
}
