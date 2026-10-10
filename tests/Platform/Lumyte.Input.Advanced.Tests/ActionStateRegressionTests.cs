using System.Numerics;
using Lumyte.Input.Actions;
using Xunit;

namespace Lumyte.Input.Advanced.Tests;

/// <summary>Verifies device selection, release boundaries and scoped interruptions.</summary>
public sealed class ActionStateRegressionTests
{
    /// <summary>Verifies input released while deselected cannot survive reselection.</summary>
    [Fact]
    public void ReselectionUsesChangesObservedWhileDeviceWasDeselected()
    {
        ActionSystem actions = CreateMovement();
        actions.SetDevices(new[] { new InputDeviceId(1) });
        actions.Advance(new[] { Stick(1, 0, Vector2.UnitX), KeyRecord(2, 0, Key.Space, true) }, At(0));
        actions.SetDevices(new[] { new InputDeviceId(2) });
        actions.Advance(new[] { Stick(3, 1, Vector2.Zero), KeyRecord(4, 1, Key.Space, false) }, At(1));
        actions.SetDevices(new[] { new InputDeviceId(1) });
        actions.Advance(ReadOnlyMemory<InputRecord>.Empty, At(2));
        Assert.Equal(Vector2.Zero, actions.GetState("move").Value);
        Assert.Equal(Vector2.Zero, actions.GetState("jump").Value);

        actions.Advance(new[] { KeyRecord(5, 3, Key.Space, true) }, At(3));
        Assert.Equal(Vector2.UnitX, actions.GetState("jump").Value);
    }

    /// <summary>Verifies selection observes analog values but suppresses already held buttons.</summary>
    [Fact]
    public void SelectionUsesCurrentAxesAndSuppressesButtonsPressedWhileDeselected()
    {
        ActionSystem actions = CreateMovement();
        actions.SetDevices(new[] { new InputDeviceId(2) });
        actions.Advance(new[] { Stick(1, 0, Vector2.UnitX), KeyRecord(2, 0, Key.Space, true) }, At(0));
        Assert.Equal(Vector2.Zero, actions.GetState("move").Value);
        Assert.Equal(Vector2.Zero, actions.GetState("jump").Value);

        actions.SetDevices(new[] { new InputDeviceId(1) });
        actions.Advance(ReadOnlyMemory<InputRecord>.Empty, At(1));
        Assert.Equal(Vector2.UnitX, actions.GetState("move").Value);
        Assert.Equal(Vector2.Zero, actions.GetState("jump").Value);
        actions.Advance(new[] { KeyRecord(3, 2, Key.Space, false), KeyRecord(4, 3, Key.Space, true) }, At(3));
        Assert.Equal(Vector2.UnitX, actions.GetState("jump").Value);
    }

    /// <summary>Verifies an unselected device disconnection also clears its cached controls.</summary>
    [Fact]
    public void DisconnectedDeselectedDeviceCannotRestoreCachedInput()
    {
        ActionSystem actions = CreateMovement();
        actions.SetDevices(new[] { new InputDeviceId(1) });
        actions.Advance(new[] { Stick(1, 0, Vector2.UnitX) }, At(0));
        actions.SetDevices(new[] { new InputDeviceId(2) });
        actions.Advance(new[] { Disconnect(2, 1, 1) }, At(1));
        actions.SetDevices(new[] { new InputDeviceId(1) });
        actions.Advance(ReadOnlyMemory<InputRecord>.Empty, At(2));
        Assert.Equal(Vector2.Zero, actions.GetState("move").Value);
    }

    /// <summary>Verifies the release threshold includes its boundary, including neutral zero.</summary>
    /// <param name="releaseThreshold">The release threshold.</param>
    [Theory]
    [InlineData(0f)]
    [InlineData(0.4f)]
    public void TriggerReleasesAtConfiguredThreshold(float releaseThreshold)
    {
        ActionSystem actions = CreateTrigger(releaseThreshold);
        actions.Advance(new[] { Trigger(1, 0, 0.6f) }, At(0));
        Assert.Equal(Vector2.UnitX, actions.GetState("fire").Value);
        actions.Advance(new[] { Trigger(2, 1, releaseThreshold) }, At(1));
        Assert.Equal(Vector2.Zero, actions.GetState("fire").Value);
        actions.Advance(new[] { Trigger(3, 2, 0.45f) }, At(2));
        Assert.Equal(Vector2.Zero, actions.GetState("fire").Value);
    }

    /// <summary>Verifies disconnection cancels only operations containing the disconnected device.</summary>
    [Fact]
    public void DisconnectionPreservesOtherDevicesHeldOperations()
    {
        var actions = new ActionSystem(new ActionProfile(
            [new ActionDefinition("a", ActionValueKind.Button), new ActionDefinition("b", ActionValueKind.Button)],
            [new ActionBinding("a", "a", "game", InputControl.ForKey(Key.A), Vector2.UnitX), new ActionBinding("b", "b", "game", InputControl.ForKey(Key.B), Vector2.UnitX)],
            [new InputContext("game")],
            [new RecognitionDefinition("hold-a", "game", RecognitionKind.Hold, ["a"], At(50)), new RecognitionDefinition("hold-b", "game", RecognitionKind.Hold, ["b"], At(50))]));
        actions.ActivateContext("game");
        actions.Advance(new[] { KeyRecord(1, 0, Key.A, true), KeyRecord(2, 0, Key.B, true, 2) }, At(0));
        actions.Advance(new[] { Disconnect(3, 10, 1) }, At(10));
        actions.Advance(ReadOnlyMemory<InputRecord>.Empty, At(60));

        Assert.Equal(Vector2.Zero, actions.GetState("a").Value);
        Assert.Equal(Vector2.UnitX, actions.GetState("b").Value);
        Assert.False(actions.Buffer.TryConsume("hold-a", At(60), out _));
        Assert.True(actions.Buffer.TryConsume("hold-b", At(60), out RecognizedAction? held));
        Assert.Equal(new[] { new InputDeviceId(2) }, held!.Devices);
    }

    /// <summary>Verifies unrelated disconnection preserves hysteresis and custom pipeline state.</summary>
    [Fact]
    public void UnrelatedDisconnectionPreservesCustomPipelinesAndHysteresis()
    {
        ActionSystem actions = CreateTrigger(0.4f);
        var processor = new CountingProcessor();
        var recognizer = new DelayedRecognizer();
        actions.SetValueProcessors("game", "fire", new[] { processor });
        actions.SetRecognizers("game", new[] { recognizer });
        actions.Advance(new[] { Trigger(1, 0, 0.6f), Trigger(2, 1, 0.45f) }, At(1));
        int resetCount = processor.ResetCount;
        actions.Advance(new[] { Disconnect(3, 10, 2) }, At(10));
        actions.Advance(ReadOnlyMemory<InputRecord>.Empty, At(60));

        Assert.Equal(Vector2.UnitX, actions.GetState("fire").Value);
        Assert.Equal(resetCount, processor.ResetCount);
        Assert.True(actions.Buffer.TryConsume("custom-hold", At(60), out _));
    }

    /// <summary>Verifies a contributing disconnection resets custom recognition and correction state.</summary>
    [Fact]
    public void ContributingDisconnectionResetsCustomPipelines()
    {
        ActionSystem actions = CreateTrigger(0.4f);
        var processor = new CountingProcessor();
        var recognizer = new DelayedRecognizer();
        actions.SetValueProcessors("game", "fire", new[] { processor });
        actions.SetRecognizers("game", new[] { recognizer });
        actions.Advance(new[] { Trigger(1, 0, 0.6f) }, At(0));
        int resetCount = processor.ResetCount;
        actions.Advance(new[] { Disconnect(2, 10, 1) }, At(10));
        actions.Advance(ReadOnlyMemory<InputRecord>.Empty, At(60));

        Assert.Equal(Vector2.Zero, actions.GetState("fire").Value);
        Assert.Equal(resetCount + 1, processor.ResetCount);
        Assert.False(actions.Buffer.TryConsume("custom-hold", At(60), out _));
    }

    /// <summary>Verifies capture survives other devices and ends when its candidate device disconnects.</summary>
    [Fact]
    public void RebindOnlyEndsForItsCapturedDevice()
    {
        ActionSystem actions = CreateMovement();
        RebindSession session = actions.BeginRebind("jump", new RebindOptions(At(100)));
        actions.Advance(new[] { Disconnect(1, 0, 2), KeyRecord(2, 1, Key.A, true) }, At(1));
        Assert.False(session.IsComplete);
        Assert.Equal(InputControl.ForKey(Key.A), session.Candidate);
        actions.Advance(new[] { Disconnect(3, 2, 3) }, At(2));
        Assert.False(session.IsComplete);
        actions.Advance(new[] { Disconnect(4, 3, 1) }, At(3));
        Assert.True(session.IsComplete);
    }

    private static ActionSystem CreateMovement()
    {
        var actions = new ActionSystem(new ActionProfile(
            [new ActionDefinition("move", ActionValueKind.Axis2D), new ActionDefinition("jump", ActionValueKind.Button)],
            [new ActionBinding("move", "move", "game", new InputControl(InputControlKind.ControllerStick, (int)ControllerStick.Left), Vector2.One), new ActionBinding("jump", "jump", "game", InputControl.ForKey(Key.Space), Vector2.UnitX)],
            [new InputContext("game")],
            []));
        actions.ActivateContext("game");
        return actions;
    }

    private static ActionSystem CreateTrigger(float releaseThreshold)
    {
        var actions = new ActionSystem(new ActionProfile(
            [new ActionDefinition("fire", ActionValueKind.Button)],
            [new ActionBinding("trigger", "fire", "game", new InputControl(InputControlKind.ControllerTrigger, (int)ControllerTrigger.Right), Vector2.UnitX, ReleaseThreshold: releaseThreshold)],
            [new InputContext("game")],
            []));
        actions.ActivateContext("game");
        return actions;
    }

    private static InputRecord Stick(ulong sequence, int milliseconds, Vector2 value)
        => new(new InputDeviceId(1), sequence, At(milliseconds), new ControllerStickData(ControllerStick.Left, value));

    private static InputRecord Trigger(ulong sequence, int milliseconds, float value)
        => new(new InputDeviceId(1), sequence, At(milliseconds), new ControllerTriggerData(ControllerTrigger.Right, value));

    private static InputRecord KeyRecord(ulong sequence, int milliseconds, Key key, bool down, ulong device = 1)
        => new(new InputDeviceId(device), sequence, At(milliseconds), new KeyData(key, down, false));

    private static InputRecord Disconnect(ulong sequence, int milliseconds, ulong device)
        => new(new InputDeviceId(device), sequence, At(milliseconds), new DeviceDisconnectedData());

    private static TimeSpan At(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

    private sealed class CountingProcessor : IActionValueProcessor
    {
        public int ResetCount { get; private set; }

        public ActionState Process(ActionState mapped, TimeSpan now) => mapped;

        public void Reset() => ResetCount++;
    }

    private sealed class DelayedRecognizer : IActionRecognizer
    {
        private ActionEvent? _started;

        public IReadOnlyList<RecognizedAction> Advance(IReadOnlyList<ActionEvent> events, TimeSpan now)
        {
            foreach (ActionEvent action in events)
            {
                if (action.Phase == ActionPhase.Started)
                {
                    _started = action;
                }
                else if (action.Phase == ActionPhase.Canceled)
                {
                    _started = null;
                }
            }

            if (_started is not null && now - _started.At >= At(50))
            {
                var result = new RecognizedAction("custom-hold", _started.ContextId, _started.At + At(50), _started.Value, _started.Devices);
                _started = null;
                return new[] { result };
            }

            return [];
        }

        public void Reset() => _started = null;
    }
}
