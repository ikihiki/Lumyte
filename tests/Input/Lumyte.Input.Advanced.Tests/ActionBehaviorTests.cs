using System.Numerics;
using Lumyte.Input.Actions;
using Lumyte.Input.Processing;
using Xunit;

namespace Lumyte.Input.Advanced.Tests;

/// <summary>Verifies combined operations, corrections and arbitration.</summary>
public sealed class ActionBehaviorTests
{
    /// <summary>Verifies chord recognition across devices.</summary>
    [Fact]
    public void ChordRecognizesMultipleDevices()
    {
        ActionSystem actions = Create(RecognitionKind.Chord);
        actions.Advance(new[] { KeyRecord(1, 0, Key.A, true, 1), KeyRecord(2, 10, Key.B, true, 2) }, At(10));
        Assert.True(actions.Buffer.TryConsume("combo", At(10), out RecognizedAction? combo));
        Assert.Equal(new ulong[] { 1, 2 }, combo!.Devices.Select(device => device.Value).Order());
    }

    /// <summary>Verifies ordered operation recognition and timeout.</summary>
    [Fact]
    public void SequenceRequiresOrderWithinWindow()
    {
        ActionSystem actions = Create(RecognitionKind.Sequence);
        actions.Advance(new[] { KeyRecord(1, 0, Key.B, true), KeyRecord(2, 1, Key.B, false), KeyRecord(3, 2, Key.A, true) }, At(2));
        Assert.False(actions.Buffer.TryConsume("combo", At(2), out _));
        actions.Advance(new[] { KeyRecord(4, 120, Key.B, true) }, At(120));
        Assert.False(actions.Buffer.TryConsume("combo", At(120), out _));
        actions.Advance(new[] { KeyRecord(5, 121, Key.A, false), KeyRecord(6, 122, Key.B, false), KeyRecord(7, 123, Key.A, true), KeyRecord(8, 124, Key.B, true) }, At(124));
        Assert.True(actions.Buffer.TryConsume("combo", At(124), out _));
    }

    /// <summary>Verifies context removal preserves unrelated completed operations.</summary>
    [Fact]
    public void ContextRemovalKeepsOtherContextBuffer()
    {
        var profile = new ActionProfile(
            [new ActionDefinition("a", ActionValueKind.Button), new ActionDefinition("b", ActionValueKind.Button)],
            [new ActionBinding("a", "a", "game", InputControl.ForKey(Key.A), Vector2.UnitX), new ActionBinding("b", "b", "chat", InputControl.ForKey(Key.B), Vector2.UnitX)],
            [new InputContext("game"), new InputContext("chat")],
            [new RecognitionDefinition("game-press", "game", RecognitionKind.Press, ["a"], At(100)), new RecognitionDefinition("chat-press", "chat", RecognitionKind.Press, ["b"], At(100))]);
        var actions = new ActionSystem(profile);
        actions.ActivateContext("game");
        actions.ActivateContext("chat");
        actions.Advance(new[] { KeyRecord(1, 0, Key.A, true), KeyRecord(2, 1, Key.B, true) }, At(1));
        actions.DeactivateContext("chat");
        actions.Advance(ReadOnlyMemory<InputRecord>.Empty, At(2));
        Assert.True(actions.Buffer.TryConsume("game-press", At(2), out _));
        Assert.False(actions.Buffer.TryConsume("chat-press", At(2), out _));
        Assert.Equal(Vector2.UnitX, actions.GetState("a").Value);
    }

    /// <summary>Verifies selected devices constrain mapping and capture.</summary>
    [Fact]
    public void PlayerSelectionFiltersMappingAndCapture()
    {
        ActionSystem actions = Create(RecognitionKind.Sequence);
        actions.SetDevices(new[] { new InputDeviceId(1) });
        RebindSession session = actions.BeginRebind("a", new RebindOptions(At(100)));
        actions.Advance(new[] { KeyRecord(1, 0, Key.J, true, 2), KeyRecord(2, 1, Key.K, true, 1) }, At(1));
        Assert.Equal(InputControl.ForKey(Key.K), session.Candidate);
    }

    /// <summary>Verifies rebind conflict policies preserve or replace competing bindings.</summary>
    [Fact]
    public void RebindReportsAndResolvesConflicts()
    {
        ActionSystem actions = Create(RecognitionKind.Sequence);
        RebindSession session = actions.BeginRebind("a", new RebindOptions(At(100)));
        actions.Advance(new[] { KeyRecord(1, 0, Key.B, true) }, At(0));
        Assert.Equal(new[] { "b" }, session.Conflicts);
        Assert.Throws<InvalidOperationException>(() => session.PrepareProfile(RebindConflictPolicy.Reject));
        Assert.Equal(2, session.PrepareProfile(RebindConflictPolicy.Allow).Bindings.Length);
        Assert.Single(session.PrepareProfile(RebindConflictPolicy.ReplaceConflicts).Bindings);
        Assert.Equal(2, actions.ExportProfile().Bindings.Length);
    }

    /// <summary>Verifies independent stick smoothing uses elapsed time and resets.</summary>
    [Fact]
    public void StickSmoothingUsesTimeAndReset()
    {
        var smoothing = new ExponentialStickSmoothing(1);
        smoothing.Process(new InputData[] { new ControllerStickData(ControllerStick.Left, Vector2.Zero) }, TimeSpan.Zero);
        IReadOnlyList<InputData> output = smoothing.Process(new InputData[] { new ControllerStickData(ControllerStick.Left, Vector2.UnitX) }, TimeSpan.FromSeconds(1));
        Assert.Equal(1 - MathF.Exp(-1), Assert.IsType<ControllerStickData>(output[0]).Value.X, 5);
        smoothing.Reset();
        output = smoothing.Process(new InputData[] { new ControllerStickData(ControllerStick.Left, Vector2.UnitX) }, TimeSpan.FromSeconds(2));
        Assert.Equal(Vector2.UnitX, Assert.IsType<ControllerStickData>(output[0]).Value);
    }

    /// <summary>Verifies mapped value processors and empty-batch recognizers interact.</summary>
    [Fact]
    public void CustomPipelinesReceiveMappedValuesAndTimers()
    {
        var profile = new ActionProfile(
            [new ActionDefinition("move", ActionValueKind.Axis2D)],
            [new ActionBinding("move-key", "move", "game", InputControl.ForKey(Key.A), Vector2.UnitX)],
            [new InputContext("game")],
            []);
        var actions = new ActionSystem(profile);
        var recognizer = new TimerRecognizer();
        actions.ActivateContext("game");
        actions.SetValueProcessors("game", "move", new[] { new HalfValueProcessor() });
        actions.SetRecognizers("game", new[] { recognizer });
        actions.Advance(new[] { KeyRecord(1, 0, Key.A, true) }, At(0));
        Assert.Equal(new Vector2(0.5f, 0), actions.GetState("move").Value);
        actions.Advance(ReadOnlyMemory<InputRecord>.Empty, At(10));
        Assert.Equal(2, recognizer.CallCount);
        Assert.True(actions.Buffer.TryConsume("custom", At(10), out _));
    }

    /// <summary>Verifies hysteresis is evaluated per contributing device.</summary>
    [Fact]
    public void TriggerHysteresisKeepsPressedDeviceActive()
    {
        var actions = new ActionSystem(new ActionProfile(
            [new ActionDefinition("fire", ActionValueKind.Button)],
            [new ActionBinding("trigger", "fire", "game", new InputControl(InputControlKind.ControllerTrigger, (int)ControllerTrigger.Right), Vector2.UnitX)],
            [new InputContext("game")],
            []));
        actions.ActivateContext("game");
        actions.Advance(new[] { Trigger(1, 0.6f, 1), Trigger(2, 0, 2), Trigger(3, 0.45f, 1) }, At(3));
        Assert.Equal(Vector2.UnitX, actions.GetState("fire").Value);
        actions.Advance(new[] { Trigger(4, 0.3f, 1) }, At(4));
        Assert.Equal(Vector2.Zero, actions.GetState("fire").Value);
    }

    /// <summary>Verifies the configured capture cancellation key is accepted during Advance.</summary>
    [Fact]
    public void RebindCancelKeyEndsCaptureWithoutApplying()
    {
        ActionSystem actions = Create(RecognitionKind.Sequence);
        RebindSession session = actions.BeginRebind("a", new RebindOptions(At(100)));
        actions.Advance(new[] { KeyRecord(1, 0, Key.Escape, true) }, At(0));
        Assert.True(session.IsComplete);
        Assert.Null(session.Candidate);
        Assert.Equal(InputControl.ForKey(Key.A), actions.ExportProfile().Bindings[0].Control);
    }

    /// <summary>Verifies sequences retain all contributing devices after normal releases.</summary>
    [Fact]
    public void SequenceRetainsEarlierDeviceContribution()
    {
        ActionSystem actions = Create(RecognitionKind.Sequence);
        actions.Advance(new[] { KeyRecord(1, 0, Key.A, true, 1), KeyRecord(2, 1, Key.A, false, 1), KeyRecord(3, 2, Key.B, true, 2) }, At(2));
        Assert.True(actions.Buffer.TryConsume("combo", At(2), out RecognizedAction? combo));
        Assert.Equal(new ulong[] { 1, 2 }, combo!.Devices.Select(device => device.Value).Order());
    }

    /// <summary>Verifies a history reset cannot reactivate an unobserved held key from repeats.</summary>
    [Fact]
    public void ResetDoesNotRecreateAnActionFromRepeat()
    {
        ActionSystem actions = Create(RecognitionKind.Sequence);
        actions.Advance(new[] { KeyRecord(1, 0, Key.A, true) }, At(0));
        actions.Reset(new InputDeviceId(1), At(1));
        actions.Advance(new[] { new InputRecord(new InputDeviceId(1), 2, At(1), new KeyData(Key.A, true, true)) }, At(1));
        Assert.Equal(Vector2.Zero, actions.GetState("a").Value);
    }

    /// <summary>Verifies smoothing converges even when a backend emits no further changed samples.</summary>
    [Fact]
    public void StickSmoothingAdvancesOnEmptyBatches()
    {
        var smoothing = new ExponentialStickSmoothing(1);
        smoothing.Process(new InputData[] { new ControllerStickData(ControllerStick.Left, Vector2.Zero) }, TimeSpan.Zero);
        smoothing.Process(new InputData[] { new ControllerStickData(ControllerStick.Left, Vector2.UnitX) }, TimeSpan.FromSeconds(1));
        IReadOnlyList<InputData> output = smoothing.Process([], TimeSpan.FromSeconds(2));
        Assert.Equal(1 - MathF.Exp(-2), Assert.IsType<ControllerStickData>(Assert.Single(output)).Value.X, 5);
        smoothing.Process(new InputData[] { new FocusData(false) }, TimeSpan.FromSeconds(3));
        Assert.Empty(smoothing.Process([], TimeSpan.FromSeconds(4)));
    }

    private static InputRecord Trigger(ulong sequence, float value, ulong device)
        => new(new InputDeviceId(device), sequence, At((int)sequence), new ControllerTriggerData(ControllerTrigger.Right, value));

    private static ActionSystem Create(RecognitionKind recognition)
    {
        var actions = new ActionSystem(new ActionProfile(
            [new ActionDefinition("a", ActionValueKind.Button), new ActionDefinition("b", ActionValueKind.Button)],
            [new ActionBinding("a", "a", "game", InputControl.ForKey(Key.A), Vector2.UnitX), new ActionBinding("b", "b", "game", InputControl.ForKey(Key.B), Vector2.UnitX)],
            [new InputContext("game")],
            [new RecognitionDefinition("combo", "game", recognition, ["a", "b"], At(100))]));
        actions.ActivateContext("game");
        return actions;
    }

    private static InputRecord KeyRecord(ulong sequence, int milliseconds, Key key, bool down, ulong device = 1)
        => new(new InputDeviceId(device), sequence, At(milliseconds), new KeyData(key, down, false));

    private static TimeSpan At(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

    private sealed class HalfValueProcessor : IActionValueProcessor
    {
        public ActionState Process(ActionState mapped, TimeSpan now) => mapped with { Value = mapped.Value * 0.5f };

        public void Reset()
        {
        }
    }

    private sealed class TimerRecognizer : IActionRecognizer
    {
        public int CallCount { get; private set; }

        public IReadOnlyList<RecognizedAction> Advance(IReadOnlyList<ActionEvent> events, TimeSpan now)
        {
            CallCount++;
            return CallCount == 2 ? new[] { new RecognizedAction("custom", "game", now, Vector2.UnitX, []) } : [];
        }

        public void Reset()
        {
        }
    }
}
