using System.Numerics;
using BenchmarkDotNet.Attributes;
using Lumyte.Input.Abstractions;
using static Lumyte.Input.Actions.Compose;

namespace Lumyte.Input.Actions.Benchmark;

/// <summary>Finite construction, evaluation and bounded recognition waves.</summary>
[BenchmarkCategory("Input", "Actions", "Screening", "Comparison", "Diagnostic")]
public class ActionBenchmarks
{
    private ActionSystem _system = null!;
    private ActionProfile _profile = null!;
    private ulong _sequence;
    private long _milliseconds;

    /// <summary>Gets or sets the recognition algorithm.</summary>
    [Params(RecognitionKind.Press, RecognitionKind.Hold, RecognitionKind.MultiTap, RecognitionKind.Chord, RecognitionKind.Sequence)]
    public RecognitionKind Recognition { get; set; }

    /// <summary>Creates a finite inherited profile with button and analog controls.</summary>
    /// <param name="kind">The recognition algorithm.</param>
    /// <returns>The immutable profile.</returns>
    public static ActionProfile CreateProfile(RecognitionKind kind) => new(
        [new("a", ActionValueKind.Button), new("b", ActionValueKind.Button), new("move", ActionValueKind.Axis2D)],
        [new("a", "a", "common", InputControl.ForKey(Key.A), Vector2.UnitX), new("b", "b", "game", InputControl.ForKey(Key.B), Vector2.UnitX), new("stick", "move", "common", new(InputControlKind.ControllerStick, (int)ControllerStick.Left), Vector2.One), new("overlay-a", "a", "overlay", InputControl.ForKey(Key.A), Vector2.UnitX)],
        [new("common"), new("game", ParentId: "common"), new("overlay", Priority: 10, Exclusive: true)],
        [new("combo", "game", kind, kind is RecognitionKind.Chord or RecognitionKind.Sequence ? ["a", "b"] : ["a"], TimeSpan.FromMilliseconds(50))]);

    /// <summary>Creates the steady-state system outside measurement.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _sequence = 0;
        _milliseconds = 0;
        _profile = CreateProfile(Recognition);
        _system = new ActionSystem(_profile);
        _system.QueueContextActivation("game");
        _system.Advance(ReadOnlyMemory<InputRecord>.Empty, TimeSpan.Zero);
    }

    /// <summary>Releases the action owner.</summary>
    [GlobalCleanup]
    public void Cleanup() => _system.Dispose();

    /// <summary>Builds and validates a generated composition snapshot.</summary>
    /// <returns>The measured result.</returns>
    [Benchmark]
    public ActionProfile BuildComposition() => Profile()[
        Profile.Actions()[Action("a", ActionValueKind.Button), Action("b", ActionValueKind.Button)],
        Profile.Contexts()[Context(id: "game")],
        Profile.Bindings()[Binding(id: "a", actionId: "a", control: InputControl.ForKey(Key.A), contextId: "game"), Binding(id: "b", actionId: "b", control: InputControl.ForKey(Key.B), contextId: "game")],
        Profile.Recognitions()[RecognitionNode()]].Build();

    /// <summary>Constructs runtime indices without input evaluation.</summary>
    /// <returns>The measured result.</returns>
    [Benchmark]
    public int ConstructRuntime()
    {
        using var system = new ActionSystem(_profile);
        return system.ExportProfile().Bindings.Length;
    }

    /// <summary>Includes construction, first activation and first recognition.</summary>
    /// <returns>The measured result.</returns>
    [Benchmark]
    public int ColdRecognition()
    {
        var workload = new ActionBenchmarks { Recognition = Recognition };
        workload.Setup();
        try
        {
            return workload.RecognitionWave();
        }
        finally
        {
            workload.Cleanup();
        }
    }

    /// <summary>Rearms all recognizers each wave and consumes completed operations.</summary>
    /// <returns>The measured result.</returns>
    [Benchmark]
    public int RecognitionWave()
    {
        TimeSpan start = Next(300);
        _system.Reset(new InputDeviceId(1), start);
        _system.Advance(new[] { Record(Key.A, true, start) }, start);
        TimeSpan next = Next(10);
        _system.Advance(new[] { Record(Key.A, false, next), Record(Key.B, true, next), Record(Key.A, true, next) }, next);
        TimeSpan end = Next(60);
        _system.Advance(ReadOnlyMemory<InputRecord>.Empty, end);
        int count = 0;
        while (_system.Buffer.TryConsume("combo", end, out RecognizedAction? action))
        {
            count += action.Devices.Length;
        }

        return count;
    }

    /// <summary>Exercises inherited analog mapping and exclusive priority on every wave.</summary>
    /// <returns>The measured result.</returns>
    [Benchmark]
    public float ContextAndAnalogWave()
    {
        TimeSpan now = Next(300);
        _system.Reset(new InputDeviceId(1), now);
        _system.QueueContextActivation("overlay");
        _system.Advance(new[] { Record(Key.A, true, now), Analog(new Vector2(0.25f, 0.5f), now) }, now);
        float claimed = _system.GetState("overlay", "a").Value.X;
        _system.QueueContextDeactivation("overlay");
        now = Next(1);
        _system.Advance(new[] { Record(Key.A, false, now), Record(Key.A, true, now), Analog(new Vector2(0.5f, 0.25f), now) }, now);
        return claimed + _system.GetState("move").Value.X;
    }

    /// <summary>Captures, prepares and confirms a replacement at the next boundary.</summary>
    /// <returns>The measured result.</returns>
    [Benchmark]
    public int RebindWave()
    {
        TimeSpan now = Next(300);
        _system.Reset(new InputDeviceId(1), now);
        Key key = _system.ExportProfile().Bindings[0].Control == InputControl.ForKey(Key.J) ? Key.K : Key.J;
        RebindSession session = _system.BeginRebind("a", new RebindOptions(TimeSpan.FromSeconds(1)));
        _system.Advance(new[] { Record(key, true, now) }, now);
        ActionProfile candidate = session.PrepareProfile(RebindConflictPolicy.Allow);
        session.Confirm(RebindConflictPolicy.Allow);
        int pending = _system.ExportProfile().Bindings[0].Control.Index;
        _system.Advance(ReadOnlyMemory<InputRecord>.Empty, Next(1));
        return candidate.Bindings[0].Control.Index + pending + _system.ExportProfile().Bindings[0].Control.Index;
    }

    /// <summary>Consumes, expires and interrupts completed operations in separate waves.</summary>
    /// <returns>The measured result.</returns>
    [Benchmark]
    public int BufferWave()
    {
        TimeSpan now = Next(300);
        _system.Buffer.Clear();
        _system.Buffer.Add(new RecognizedAction("manual", "game", now, Vector2.UnitX, [new InputDeviceId(1)]), now);
        int consumed = _system.Buffer.TryConsume("manual", now, out _) ? 1 : 0;
        _system.Buffer.Add(new RecognizedAction("manual", "game", now, Vector2.UnitX, [new InputDeviceId(1)]), now);
        _system.Buffer.Prune(Next(250));
        int expired = _system.Buffer.Count;
        now = Next(1);
        _system.Buffer.Add(new RecognizedAction("manual", "game", now, Vector2.UnitX, [new InputDeviceId(1)]), now);
        _system.Reset(new InputDeviceId(1), now);
        _system.Advance(ReadOnlyMemory<InputRecord>.Empty, now);
        return consumed + expired + _system.Buffer.Count;
    }

    private Compose.Definitions.Recognition RecognitionNode() => Compose.Recognition(id: "combo", kind: Recognition, actions: ["a"], window: TimeSpan.FromMilliseconds(50), contextId: "game");

    private TimeSpan Next(int milliseconds) => TimeSpan.FromMilliseconds(_milliseconds += milliseconds);

    private InputRecord Record(Key key, bool down, TimeSpan at) => new(new InputDeviceId(1), ++_sequence, at, new KeyData(key, down, false));

    private InputRecord Analog(Vector2 value, TimeSpan at) => new(new InputDeviceId(1), ++_sequence, at, new ControllerStickData(ControllerStick.Left, value));
}
