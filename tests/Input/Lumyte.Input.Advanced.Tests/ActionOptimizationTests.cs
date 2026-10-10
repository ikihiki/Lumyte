using System.Numerics;
using Lumyte.Input.Actions;
using Xunit;

namespace Lumyte.Input.Advanced.Tests;

/// <summary>Verifies indexed evaluation preserves snapshots, ordering and update semantics.</summary>
public sealed class ActionOptimizationTests
{
    /// <summary>Verifies steady input evaluation reuses its state and contributor snapshots.</summary>
    /// <param name="held">Whether the control remains active during empty updates.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WarmIdleAdvancesDoNotAllocate(bool held)
    {
        var actions = new ActionSystem(StickProfile());
        actions.ActivateContext("game");
        actions.Advance(new[] { Stick(1, 0, 1, held ? Vector2.UnitX : Vector2.Zero) }, At(0));
        for (int i = 0; i < 1000; i++)
        {
            actions.Advance(ReadOnlyMemory<InputRecord>.Empty, At(i));
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 1000; i < 2000; i++)
        {
            actions.Advance(ReadOnlyMemory<InputRecord>.Empty, At(i));
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.Equal(held ? Vector2.UnitX : Vector2.Zero, actions.GetState("move").Value);
    }

    /// <summary>Verifies polling existing and neutral states allocates no managed memory.</summary>
    [Fact]
    public void WarmPollingDoesNotAllocate()
    {
        var actions = new ActionSystem(StickProfile());
        actions.ActivateContext("game");
        actions.Advance(new[] { Stick(1, 0, 1, Vector2.UnitX) }, At(0));
        var inactive = new ActionSystem(StickProfile());
        for (int i = 0; i < 1000; i++)
        {
            actions.GetState("move");
            inactive.GetState("move");
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            actions.GetState("move");
            inactive.GetState("move");
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    /// <summary>Verifies empty buffer pruning and polling allocate no managed memory.</summary>
    [Fact]
    public void EmptyBufferUpdatesDoNotAllocate()
    {
        var buffer = new ActionInputBuffer(new InputBufferOptions(At(100), 8));
        for (int i = 0; i < 1000; i++)
        {
            buffer.Prune(At(0));
            buffer.TryConsume("missing", At(0), out _);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            buffer.Prune(At(0));
            buffer.TryConsume("missing", At(0), out _);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    /// <summary>Verifies pruning preserves insertion order when timestamps are not sorted.</summary>
    [Fact]
    public void BufferPruningKeepsOldestMatchingInsertionAndExpiryBoundary()
    {
        var buffer = new ActionInputBuffer(new InputBufferOptions(At(10), 8));
        var first = new RecognizedAction("press", "game", At(9), Vector2.UnitX, []);
        var last = new RecognizedAction("press", "game", At(8), Vector2.UnitY, []);
        buffer.Add(first, At(9));
        buffer.Add(new RecognizedAction("press", "game", At(0), Vector2.Zero, []), At(9));
        buffer.Add(last, At(9));
        buffer.Prune(At(10));
        Assert.Equal(2, buffer.Count);
        Assert.True(buffer.TryConsume("press", At(10), out RecognizedAction? consumed));
        Assert.Same(first, consumed);
        Assert.True(buffer.TryConsume("press", At(10), out consumed));
        Assert.Same(last, consumed);
        Assert.False(buffer.TryConsume("press", At(10), out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Prune(At(9)));
    }

    /// <summary>Verifies unchanged values still track replacement contributors without mutating snapshots.</summary>
    /// <param name="analog">Whether to use an analog control instead of a button.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EqualValuesTrackDeviceChangesAndKeepPublishedSnapshots(bool analog)
    {
        ActionValueKind kind = analog ? ActionValueKind.Axis2D : ActionValueKind.Button;
        InputControl control = analog ? new InputControl(InputControlKind.ControllerStick, (int)ControllerStick.Left) : InputControl.ForKey(Key.A);
        var actions = new ActionSystem(new ActionProfile(
            [new ActionDefinition("move", kind)],
            [new ActionBinding("move", "move", "game", control, Vector2.One)],
            [new InputContext("game")],
            []));
        var events = new List<ActionEvent>();
        actions.Changed += events.Add;
        actions.ActivateContext("game");
        InputData down = analog ? new ControllerStickData(ControllerStick.Left, Vector2.UnitX) : new KeyData(Key.A, true, false);
        InputData up = analog ? new ControllerStickData(ControllerStick.Left, Vector2.Zero) : new KeyData(Key.A, false, false);
        actions.Advance(new[] { Record(1, 0, 1, down) }, At(0));
        ActionState original = actions.GetState("move");
        ActionEvent started = Assert.Single(events);
        actions.Advance(new[] { Record(2, 1, 2, down), Record(3, 2, 1, up), Record(4, 3, 1, new DeviceDisconnectedData()) }, At(3));
        Assert.Single(events);
        Assert.Equal(Vector2.UnitX, actions.GetState("move").Value);

        actions.Advance(new[] { Record(5, 4, 2, up) }, At(4));
        Assert.Equal(Vector2.Zero, actions.GetState("move").Value);
        Assert.Equal(Vector2.UnitX, original.Value);
        Assert.Equal(new[] { new InputDeviceId(1) }, started.Devices);
        Assert.Equal(new[] { new InputDeviceId(2) }, events[1].Devices);
        Assert.Equal(ActionPhase.Canceled, events[1].Phase);
    }

    /// <summary>Verifies control indexing preserves device tie order after dictionary slot reuse.</summary>
    [Fact]
    public void DeviceTieOrderSurvivesRemovalAndNewControlInsertion()
    {
        var actions = new ActionSystem(StickProfile());
        actions.ActivateContext("game");
        actions.Advance(new[] { KeyRecord(1, 0, Key.Space, true), Stick(2, 0, 2, Vector2.UnitX), Stick(3, 0, 3, Vector2.UnitY) }, At(0));
        Assert.Equal(Vector2.UnitX, actions.GetState("move").Value);
        actions.Advance(new[] { Record(4, 1, 1, new DeviceDisconnectedData()), Stick(5, 1, 4, -Vector2.UnitX) }, At(1));
        Assert.Equal(-Vector2.UnitX, actions.GetState("move").Value);
    }

    /// <summary>Verifies bindings retain ordinal tie priority when a new profile is applied.</summary>
    [Fact]
    public void BindingIndexRefreshesAtProfileBoundary()
    {
        var profile = new ActionProfile(
            [new ActionDefinition("move", ActionValueKind.Axis1D)],
            [new ActionBinding("z-right", "move", "game", InputControl.ForKey(Key.A), Vector2.UnitX), new ActionBinding("a-left", "move", "game", InputControl.ForKey(Key.B), -Vector2.UnitX)],
            [new InputContext("game")],
            []);
        var actions = new ActionSystem(profile);
        actions.ActivateContext("game");
        actions.Advance(new[] { KeyRecord(1, 0, Key.A, true), KeyRecord(2, 0, Key.B, true) }, At(0));
        Assert.Equal(-Vector2.UnitX, actions.GetState("move").Value);

        ActionProfile replacement = profile with
        {
            Bindings = [new ActionBinding("z-left", "move", "game", InputControl.ForKey(Key.B), -Vector2.UnitX), new ActionBinding("a-right", "move", "game", InputControl.ForKey(Key.A), Vector2.UnitX)],
        };
        actions.ApplyProfile(replacement);
        Assert.Same(profile, actions.ExportProfile());
        actions.Advance(new[] { KeyRecord(3, 1, Key.A, false), KeyRecord(4, 1, Key.B, false), KeyRecord(5, 2, Key.B, true), KeyRecord(6, 2, Key.A, true) }, At(2));
        Assert.Same(replacement, actions.ExportProfile());
        Assert.Equal(Vector2.UnitX, actions.GetState("move").Value);
    }

    /// <summary>Verifies context ordering responds to activation, deactivation and profile changes.</summary>
    [Fact]
    public void ExclusiveContextOrderRefreshesWithConfigurationChanges()
    {
        var profile = new ActionProfile(
            [new ActionDefinition("low", ActionValueKind.Button), new ActionDefinition("high", ActionValueKind.Button)],
            [new ActionBinding("low", "low", "low", InputControl.ForKey(Key.A), Vector2.UnitX), new ActionBinding("high", "high", "high", InputControl.ForKey(Key.A), Vector2.UnitX)],
            [new InputContext("low", Exclusive: true), new InputContext("high", Exclusive: true)],
            []);
        var actions = new ActionSystem(profile);
        actions.ActivateContext("low");
        actions.ActivateContext("high");
        actions.Advance(new[] { KeyRecord(1, 0, Key.A, true) }, At(0));
        Assert.Equal(Vector2.Zero, actions.GetState("low").Value);
        Assert.Equal(Vector2.UnitX, actions.GetState("high").Value);
        actions.DeactivateContext("high");
        actions.Advance(ReadOnlyMemory<InputRecord>.Empty, At(1));
        Assert.Equal(Vector2.UnitX, actions.GetState("low").Value);

        actions.ActivateContext("high");
        actions.Advance(new[] { KeyRecord(2, 2, Key.A, false), KeyRecord(3, 3, Key.A, true) }, At(3));
        Assert.Equal(Vector2.UnitX, actions.GetState("high").Value);
        actions.ApplyProfile(profile with { Contexts = [new InputContext("low", 10, true), new InputContext("high", Exclusive: true)] });
        actions.Advance(new[] { KeyRecord(4, 4, Key.A, false), KeyRecord(5, 5, Key.A, true) }, At(5));
        Assert.Equal(Vector2.UnitX, actions.GetState("low").Value);
        Assert.Equal(Vector2.Zero, actions.GetState("high").Value);
    }

    /// <summary>Verifies polling equal-strength states keeps the original context insertion order.</summary>
    [Fact]
    public void StateTieUsesExistingContextStateOrder()
    {
        var actions = new ActionSystem(new ActionProfile(
            [new ActionDefinition("move", ActionValueKind.Axis2D)],
            [new ActionBinding("a", "move", "low", InputControl.ForKey(Key.A), Vector2.UnitX), new ActionBinding("b", "move", "high", InputControl.ForKey(Key.B), Vector2.UnitY)],
            [new InputContext("low"), new InputContext("high", 10)],
            []));
        actions.ActivateContext("low");
        actions.ActivateContext("high");
        actions.Advance(new[] { KeyRecord(1, 0, Key.A, true), KeyRecord(2, 0, Key.B, true) }, At(0));
        Assert.Equal(Vector2.UnitY, actions.GetState("move").Value);
    }

    /// <summary>Verifies processors still run for every input and the final update time.</summary>
    [Fact]
    public void ProcessorReceivesEveryRecordTimeAndEmptyUpdate()
    {
        var actions = new ActionSystem(new ActionProfile(
            [new ActionDefinition("move", ActionValueKind.Axis1D)],
            [new ActionBinding("move", "move", "game", InputControl.ForKey(Key.A), Vector2.UnitX)],
            [new InputContext("game")],
            []));
        var processor = new ObservingProcessor();
        var events = new List<ActionEvent>();
        actions.Changed += events.Add;
        actions.ActivateContext("game");
        actions.SetValueProcessors("game", "move", new[] { processor });
        actions.Advance(new[] { KeyRecord(1, 10, Key.A, true), KeyRecord(2, 20, Key.A, false) }, At(30));
        actions.Advance(ReadOnlyMemory<InputRecord>.Empty, At(40));
        Assert.Equal(new[] { At(10), At(20), At(30), At(40) }, processor.Times);
        Assert.Equal(new[] { Vector2.UnitX, Vector2.Zero, Vector2.Zero, Vector2.Zero }, processor.States.Select(state => state.Value));
        Assert.Equal(new[] { ActionPhase.Started, ActionPhase.Canceled }, events.Select(action => action.Phase));
    }

    /// <summary>Verifies delegate snapshots and exception aggregation remain per event.</summary>
    [Fact]
    public void NotificationChangesOnlyAffectFollowingEventsAndErrorsRemainAggregated()
    {
        var actions = new ActionSystem(new ActionProfile(
            [new ActionDefinition("fire", ActionValueKind.Button)],
            [new ActionBinding("fire", "fire", "game", InputControl.ForKey(Key.A), Vector2.UnitX)],
            [new InputContext("game")],
            [new RecognitionDefinition("press", "game", RecognitionKind.Press, ["fire"], At(100))]));
        var calls = new List<string>();
        Action<ActionEvent> second = action =>
        {
            calls.Add("second:" + action.Phase);
            throw new ArgumentException();
        };
        Action<ActionEvent> added = action => calls.Add("added:" + action.Phase);
        actions.Changed += action =>
        {
            calls.Add("first:" + action.Phase);
            actions.Changed -= second;
            actions.Changed += added;
            throw new InvalidOperationException();
        };
        actions.Changed += second;
        actions.Recognized += _ => calls.Add("recognized");
        actions.ActivateContext("game");
        InputRecord[] records = [KeyRecord(1, 0, Key.A, true), KeyRecord(2, 0, Key.A, false)];
        AggregateException error = Assert.Throws<AggregateException>(() => actions.Advance(records, At(0)));
        Assert.Equal(3, error.InnerExceptions.Count);
        Assert.Equal(new[] { "first:Started", "second:Started", "first:Canceled", "added:Canceled", "recognized" }, calls);
        Assert.Throws<ArgumentException>(() => actions.Advance(records, At(0)));
    }

    private static ActionProfile StickProfile()
        => new([new ActionDefinition("move", ActionValueKind.Axis2D)], [new ActionBinding("move", "move", "game", new InputControl(InputControlKind.ControllerStick, (int)ControllerStick.Left), Vector2.One)], [new InputContext("game")], []);

    private static InputRecord KeyRecord(ulong sequence, int milliseconds, Key key, bool down)
        => Record(sequence, milliseconds, 1, new KeyData(key, down, false));

    private static InputRecord Stick(ulong sequence, int milliseconds, ulong device, Vector2 value)
        => Record(sequence, milliseconds, device, new ControllerStickData(ControllerStick.Left, value));

    private static InputRecord Record(ulong sequence, int milliseconds, ulong device, InputData data)
        => new(new InputDeviceId(device), sequence, At(milliseconds), data);

    private static TimeSpan At(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

    private sealed class ObservingProcessor : IActionValueProcessor
    {
        public List<TimeSpan> Times { get; } = new();

        public List<ActionState> States { get; } = new();

        public ActionState Process(ActionState mapped, TimeSpan now)
        {
            Times.Add(now);
            States.Add(mapped);
            return mapped;
        }

        public void Reset()
        {
        }
    }
}
