using System.Numerics;
using BenchmarkDotNet.Attributes;
using Lumyte.Input.Abstractions;

namespace Lumyte.Input.Processing.Benchmark;

/// <summary>Bounded end-to-end ordered processing waves. Construction, queue population,
/// updates, state/history snapshots, history clearing and disposal are measured on one thread.</summary>
[MemoryDiagnoser]
public class ProcessingBenchmarks
{
    /// <summary>Gets or sets the bounded workload dimension.</summary>
    [Params(8, 64)]
    public int BatchSize { get; set; } = 8;

    /// <summary>Gets or sets the bounded workload dimension.</summary>
    [Params(1, 2)]
    public int WindowCount { get; set; } = 1;

    /// <summary>Executes the finite public-API workload.</summary>
    /// <returns>The consumed workload results.</returns>
    [Benchmark]
    [BenchmarkCategory("Input", "Calibration", "Screening")]
    public ProcessingWave CalibratedAnalog() => RunAnalog(false);

    /// <summary>Executes the finite public-API workload.</summary>
    /// <returns>The consumed workload results.</returns>
    [Benchmark]
    [BenchmarkCategory("Input", "Smoothing", "Comparison")]
    public ProcessingWave CalibratedAndSmoothedAnalog() => RunAnalog(true);

    /// <summary>Executes the finite public-API workload.</summary>
    /// <returns>The consumed workload results.</returns>
    [Benchmark]
    [BenchmarkCategory("Input", "VirtualController", "Diagnostic")]
    public ProcessingWave TouchGesturePipeline() => RunTouch();

    /// <summary>Two timed sample waves followed by an empty raw update that advances smoothing.
    /// Scope-local correction runs before smoothing; no already-drained batch is reused.</summary>
    /// <param name="smooth">Whether to add smoothing after calibration.</param>
    /// <returns>Processing observations.</returns>
    public ProcessingWave RunAnalog(bool smooth)
    {
        TimeSpan now = TimeSpan.Zero;
        using var raw = new Source(InputDeviceKind.Controller);
        using var corrected = new OrderedCorrectingInputSource(
            raw,
            (_, _) => smooth ? [new AnalogCorrection(new Vector2(0.1f, 0), 0.2f), new ExponentialStickSmoothing(1)] : [new AnalogCorrection(new Vector2(0.1f, 0), 0.2f)],
            () => now);
        using var input = new InputSystem([corrected]);
        raw.Analog(BatchSize, WindowCount, 0.1f, true);
        input.Update();
        now = TimeSpan.FromSeconds(1);
        raw.Analog(BatchSize, WindowCount, 0.7f, false);
        input.Update();
        now = TimeSpan.FromSeconds(2);
        input.Update();
        InputRecord[] records = input.GetRecords(raw.Id).ToArray();
        Vector2 value = input.GetState(raw.Id, raw.Windows[0]).GetStick(ControllerStick.Left);
        input.ClearRecords(raw.Id);
        return new(records.Length, value.X, 0, 0, records.Where(r => r.Capture is not null).Select(r => r.Capture!.Value.Sequence).ToArray());
    }

    /// <summary>Pressure correction followed by virtual touch-controller generation, with
    /// independent window scopes. Each contact is begun, moved and ended in one finite wave.</summary>
    /// <returns>Processing observations.</returns>
    public ProcessingWave RunTouch()
    {
        TimeSpan now = TimeSpan.Zero;
        using var raw = new Source(InputDeviceKind.Touch);
        using var corrected = new OrderedCorrectingInputSource(raw, (_, _) => [new AnalogCorrection(Vector2.Zero, pressureExponent: 2)], () => now);
        using var virtualized = new OrderedVirtualizingInputSource(corrected, (_, _) => new TouchControllerGenerator(100, 80), () => now);
        using var input = new InputSystem([virtualized]);
        raw.Touch(BatchSize, WindowCount);
        input.Update();
        InputRecord[] physical = input.GetRecords(raw.Id).ToArray();
        InputDeviceId logical = input.DeviceInfos.Single(d => d.IdentityKind == InputDeviceIdentityKind.Logical).Id;
        InputRecord[] derived = input.GetRecords(logical).ToArray();
        float pressure = physical.Select(r => r.Data).OfType<TouchData>().First().Pressure!.Value;
        int swipes = derived.Count(r => r.Data is ControllerButtonData { Button: ControllerButton.South, IsDown: true });
        int contacts = input.GetState(raw.Id).TouchContacts.Count;
        foreach (InputDeviceId id in input.DeviceInfos.Select(d => d.Id))
        {
            input.ClearRecords(id);
        }

        return new(physical.Length + derived.Length, pressure, swipes, contacts, derived.Where(r => r.Capture is not null).Select(r => r.Capture!.Value.Sequence).ToArray());
    }

    private sealed class Source(InputDeviceKind kind) : IOrderedInputSource
    {
        private InputEventBatch? _batch;
        private ulong _id;
        private ulong _sequence;

        public Guid StreamId { get; } = Guid.NewGuid();

        public InputDeviceId Id { get; private set; }

        public InputWindowId[] Windows { get; } = [new(Guid.NewGuid()), new(Guid.NewGuid())];

        public void Initialize(IInputDeviceRegistry registry) => Id = registry.RegisterDevice(new Device(kind));

        public void Analog(int count, int windows, float value, bool focus)
        {
            var events = new List<InputSourceEvent>();
            if (focus)
            {
                for (int w = 0; w < windows; w++)
                {
                    Add(events, w, new FocusData(true));
                }
            }

            for (int i = 0; i < count; i++)
            {
                Add(events, i % windows, new ControllerStickData(ControllerStick.Left, new Vector2(value, 0)));
            }

            _batch = new(++_id, _sequence, events);
        }

        public void Touch(int count, int windows)
        {
            var events = new List<InputSourceEvent>();
            for (int w = 0; w < windows; w++)
            {
                Add(events, w, new FocusData(true));
            }

            for (int i = 0; i < count; i++)
            {
                var contact = new TouchContactId((ulong)i + 1);
                Add(events, i % windows, new TouchData(contact, TouchPhase.Began, Vector2.Zero, 0.5f));
                Add(events, i % windows, new TouchData(contact, TouchPhase.Moved, new Vector2(100, 0), 0.5f));
                Add(events, i % windows, new TouchData(contact, TouchPhase.Ended, new Vector2(100, 0), 0.5f));
            }

            _batch = new(++_id, _sequence, events);
        }

        public InputEventBatch? PeekEvents() => _batch;

        public void CommitEvents(ulong batchId)
        {
            if (_batch?.Id != batchId)
            {
                throw new InvalidOperationException();
            }

            _batch = null;
        }

        public void AbortEvents() => _batch = null;

        public void Update()
        {
        }

        public void Shutdown()
        {
        }

        public void Dispose()
        {
        }

        private void Add(List<InputSourceEvent> events, int window, InputData data) => events.Add(new(Id, Windows[window], ++_sequence, data));
    }

    private sealed class Device(InputDeviceKind kind) : IInputDevice
    {
        public InputDeviceDescriptor Descriptor { get; } = new(kind, "synthetic processing device", InputDeviceIdentityKind.Physical);

        public IReadOnlyList<InputData> DrainEvents() => [];

        public void Dispose()
        {
        }
    }
}
