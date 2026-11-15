using System.Numerics;
using BenchmarkDotNet.Attributes;
using Lumyte.Input.Abstractions;
using Lumyte.Input.Actions;

namespace Lumyte.Input.Diagnostics.Benchmark;

/// <summary>End-to-end bounded consumer waves including construction, capture and disposal.
/// Each invocation owns its observers and systems on one thread; no state survives a wave.</summary>
[MemoryDiagnoser]
public class InspectionBenchmarks
{
    /// <summary>Captures current device and action views after alternating physical input.</summary>
    /// <param name="frames">The bounded frame count.</param>
    /// <returns>The observed wave receipt.</returns>
    [Benchmark]
    [BenchmarkCategory("Input", "InspectionSnapshot", "Screening")]
    [Arguments(1)]
    [Arguments(8)]
    public InspectionWave CaptureDeviceAndActionViews(int frames)
    {
        using var wave = new Consumer();
        var values = new List<float>();
        for (int i = 0; i < frames; i++)
        {
            wave.Source.Keyboard.Add(new KeyData(Key.A, i % 2 == 0, false));
            values.Add(wave.Step());
        }

        return wave.Result(values);
    }

    /// <summary>Acquires, updates and releases an action lease.</summary>
    /// <returns>The observed wave receipt.</returns>
    [Benchmark]
    [BenchmarkCategory("Input", "ActionOverride", "Comparison")]
    public InspectionWave AcquireUpdateRelease()
    {
        using var wave = new Consumer();
        InputOverrideLease lease = wave.Inspection.Acquire(wave.Owner, wave.Target, Spec(1)).Lease!;
        float first = wave.Step();
        InputOverrideResult updated = wave.Inspection.Update(wave.Owner, lease.Id, lease.Revision, Spec(0));
        float second = wave.Step();
        InputOverrideResult released = wave.Inspection.Release(wave.Owner, lease.Id, updated.Lease!.Revision);
        float third = wave.Step();
        return wave.Result([first, second, third], (updated.IsSuccess ? 1 : 0) + (released.IsSuccess ? 1 : 0) + 1);
    }

    /// <summary>Consumes an override before and after its logical expiry.</summary>
    /// <returns>The observed wave receipt.</returns>
    [Benchmark]
    [BenchmarkCategory("Input", "ActionOverrideExpiry", "Comparison")]
    public InspectionWave ExpireOnLogicalTime()
    {
        using var wave = new Consumer();
        InputOverrideResult acquired = wave.Inspection.Acquire(wave.Owner, wave.Target, Spec(1, 15));
        float first = wave.Step();
        float second = wave.Step();
        InputOverrideResult expired = wave.Inspection.GetLease(wave.Owner, acquired.Lease!.Id);
        return wave.Result([first, second], acquired.IsSuccess ? 1 : 0, expired: expired.ErrorCode == "lease-not-found" ? 1 : 0, error: expired.ErrorCode);
    }

    /// <summary>Exercises stable busy and revision-conflict rejections.</summary>
    /// <returns>The observed wave receipt.</returns>
    [Benchmark]
    [BenchmarkCategory("Input", "OverrideRejection", "Diagnostic")]
    public InspectionWave RejectBusyAndStaleRevision()
    {
        using var wave = new Consumer();
        InputOverrideLease lease = wave.Inspection.Acquire(wave.Owner, wave.Target, Spec(1)).Lease!;
        InputOverrideResult busy = wave.Inspection.Acquire(wave.Owner, wave.Target, Spec(0));
        InputOverrideResult conflict = wave.Inspection.Update(wave.Owner, lease.Id, lease.Revision + 1, Spec(0));
        float value = wave.Step();
        wave.Inspection.Release(wave.Owner, lease.Id, lease.Revision);
        return wave.Result([value], 1, (!busy.IsSuccess ? 1 : 0) + (!conflict.IsSuccess ? 1 : 0), error: busy.ErrorCode + "/" + conflict.ErrorCode);
    }

    /// <summary>Overrides a held device key and restores its physical value.</summary>
    /// <returns>The observed wave receipt.</returns>
    [Benchmark]
    [BenchmarkCategory("Input", "DeviceOverride", "Comparison")]
    public InspectionWave OverrideDeviceAndRestorePhysical()
    {
        using var wave = new Consumer();
        wave.Source.Keyboard.Add(new KeyData(Key.A, true, false));
        float physical = wave.Step();
        InputOverrideTarget target = wave.Inspection.GetDeviceTarget(wave.Input, wave.Source.Id, InputControl.ForKey(Key.A));
        InputOverrideLease lease = wave.Inspection.Acquire(wave.Owner, target, Spec(0)).Lease!;
        float overridden = wave.Step();
        InputOverrideResult released = wave.Inspection.Release(wave.Owner, lease.Id, lease.Revision);
        float restored = wave.Step();
        return wave.Result([physical, overridden, restored], released.IsSuccess ? 2 : 1);
    }

    /// <summary>Consumes press, wait and release Tape commands.</summary>
    /// <returns>The observed wave receipt.</returns>
    [Benchmark]
    [BenchmarkCategory("Input", "InputTape", "Comparison")]
    public InspectionWave PlayFrameSequence()
    {
        using var wave = new Consumer();
        var tape = new InputTape(1, [new("jump", ActionValueKind.Button)], [new(InputTapeCommandKind.Press, 0, Vector2.Zero), new(InputTapeCommandKind.WaitFrames, 0, Vector2.Zero, 1), new(InputTapeCommandKind.Release, 0, Vector2.Zero)]);
        InputTapePlayback playback = wave.Inspection.Play(wave.Owner, tape, [wave.Target], 1);
        float first = wave.Step();
        float second = wave.Step();
        InputTapePlayback completed = wave.Inspection.GetPlayback(wave.Owner, playback.Id);
        return wave.Result([first, second], 1, commands: checked((int)completed.CommandIndex), stopReason: completed.StopReason);
    }

    /// <summary>Consumes ordered live values and releases the producer.</summary>
    /// <param name="frames">The bounded frame count.</param>
    /// <returns>The observed wave receipt.</returns>
    [Benchmark]
    [BenchmarkCategory("Input", "InputLive", "Comparison")]
    [Arguments(1)]
    [Arguments(8)]
    public InspectionWave SendOrderedLiveSequence(int frames)
    {
        using var wave = new Consumer();
        InputLiveSession live = wave.Inspection.Begin(wave.Owner, [wave.Target]);
        var values = new List<float>();
        for (int i = 0; i < frames; i++)
        {
            wave.Inspection.Send(wave.Owner, live.Id, i, [new(wave.Target, i % 2 == 0 ? Vector2.UnitX : Vector2.Zero)]);
            wave.Inspection.Heartbeat(wave.Owner, live.Id);
            values.Add(wave.Step());
        }

        wave.Inspection.Stop(wave.Owner, live.Id);
        values.Add(wave.Step());
        return wave.Result(values, frames + 2);
    }

    private static InputOverrideSpec Spec(float x, int duration = 1000) => new(InputOverrideMode.Replace, new(x, 0), TimeSpan.FromMilliseconds(duration));

    private sealed class Consumer : IDisposable
    {
        private readonly Clock _clock = new();
        private long _frame;

        public Consumer()
        {
            Inspection = new();
            Source = new();
            Input = new([Source], Inspection, _clock);
            Actions = new(new([new("jump", ActionValueKind.Button)], [new("key", "jump", "game", InputControl.ForKey(Key.A), Vector2.UnitX)], [new("game")], []), null, Inspection);
            Actions.QueueContextActivation("game");
            Target = Inspection.GetActionTarget(Actions, "game", "jump");
            Inspection.BeginFrame(0);
            Source.Keyboard.Add(new FocusData(true));
            Input.Update();
            Actions.Advance(Input.GetRecords(Source.Id), TimeSpan.Zero);
            Input.ClearRecords(Source.Id);
        }

        public InputInspectionSession Inspection { get; }

        public Source Source { get; }

        public InputSystem Input { get; }

        public ActionSystem Actions { get; }

        public InputOverrideTarget Target { get; }

        public InputOverrideOwner Owner { get; } = new(Guid.NewGuid(), "benchmark");

        public float Step()
        {
            _clock.Ticks += TimeSpan.TicksPerMillisecond * 10;
            Inspection.BeginFrame(++_frame);
            Input.ClearRecords(Source.Id);
            Input.Update();
            Actions.Advance(Input.GetRecords(Source.Id), TimeSpan.FromTicks(_clock.Ticks));
            Actions.Buffer.Clear();
            return Actions.GetState("game", "jump").Value.X;
        }

        public InspectionWave Result(IEnumerable<float> values, int accepted = 0, int rejected = 0, int expired = 0, string? error = null, int commands = 0, string? stopReason = null)
            => new(Inspection.CreateSnapshot(), values.ToArray(), accepted, rejected, expired, error, Inspection.Revision, commands, stopReason);

        public void Dispose()
        {
            Inspection.NotifySessionClosed(Owner.SessionId);
            Actions.Dispose();
            Input.Dispose();
            Source.Dispose();
            Inspection.Dispose();
        }
    }

    private sealed class Clock : TimeProvider
    {
        public long Ticks { get; set; }

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Ticks;
    }

    private sealed class Source : IInputSource
    {
        public Keyboard Keyboard { get; } = new();

        public InputDeviceId Id { get; private set; }

        public void Initialize(IInputDeviceRegistry registry) => Id = registry.RegisterDevice(Keyboard);

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

    private sealed class Keyboard : IInputDevice
    {
        private readonly List<InputData> _pending = [];

        public InputDeviceDescriptor Descriptor { get; } = new(InputDeviceKind.Keyboard, "inspection keyboard", InputDeviceIdentityKind.Physical);

        public void Add(InputData data) => _pending.Add(data);

        public IReadOnlyList<InputData> DrainEvents()
        {
            InputData[] batch = _pending.ToArray();
            _pending.Clear();
            return batch;
        }

        public void Dispose() => _pending.Clear();
    }
}
