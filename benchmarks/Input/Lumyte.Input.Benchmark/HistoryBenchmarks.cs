using BenchmarkDotNet.Attributes;
using Lumyte.Input.Abstractions;

namespace Lumyte.Input.Benchmark;

/// <summary>Finite ordered waves, including synthetic queue construction, snapshots and history clearing.
/// Systems are constructed and disposed inside each invocation on the consumer thread.</summary>
[MemoryDiagnoser]
public class HistoryBenchmarks
{
    /// <summary>Gets or sets the bounded workload dimension.</summary>
    [Params(16, 128)]
    public int BatchSize { get; set; } = 16;

    /// <summary>Gets or sets the bounded workload dimension.</summary>
    [Params(1, 4)]
    public int DeviceCount { get; set; } = 1;

    /// <summary>Gets or sets the bounded workload dimension.</summary>
    [Params(1, 2)]
    public int WindowCount { get; set; } = 1;

    /// <summary>Executes the finite public-API workload.</summary>
    /// <returns>The consumed workload results.</returns>
    [Benchmark]
    [BenchmarkCategory("Input", "History", "Screening")]
    public HistoryWave OrderedIngestionAndSnapshots() => RunWave("Manual", false);

    /// <summary>Executes the finite public-API workload.</summary>
    /// <returns>The consumed workload results.</returns>
    [Benchmark]
    [BenchmarkCategory("Input", "Retention", "Comparison")]
    public HistoryWave CountRetentionAndReadGap() => RunWave("Count", false);

    /// <summary>Executes the finite public-API workload.</summary>
    /// <returns>The consumed workload results.</returns>
    [Benchmark]
    [BenchmarkCategory("Input", "Retention", "Comparison")]
    public HistoryWave TimeRetentionAndReadGap() => RunWave("Time", false);

    /// <summary>Executes the finite public-API workload.</summary>
    /// <returns>The consumed workload results.</returns>
    [Benchmark]
    [BenchmarkCategory("Input", "Notification", "Diagnostic")]
    public HistoryWave NotificationsAndFocusCancellation() => RunWave("Manual", true);

    /// <summary>Measures two queued waves and two updates, retention, cached and cursor reads,
    /// scoped state snapshots, optional notifications, and explicit final history clearing.</summary>
    /// <param name="retention">Retention profile.</param>
    /// <param name="notify">Whether to exercise notifications and cancellation.</param>
    /// <returns>History observations.</returns>
    public HistoryWave RunWave(string retention, bool notify)
    {
        var clock = new WaveClock();
        using var source = new WaveSource(DeviceCount);
        using var input = new InputSystem([source], clock);
        int notifications = 0;
        if (notify)
        {
            input.Recorded += _ => notifications++;
        }

        foreach (InputDeviceId id in source.Ids)
        {
            InputRetentionPolicy policy = retention switch
            {
                "Count" => new InputRetentionPolicy(MaxRecords: BatchSize / 2),
                "Time" => new InputRetentionPolicy(MaxAge: TimeSpan.FromMilliseconds(5)),
                _ => new InputRetentionPolicy(),
            };
            input.SetRetentionPolicy(id, policy);
        }

        source.Queue(BatchSize, WindowCount, false);
        input.Update();
        ReadOnlyMemory<InputRecord> first = input.GetRecords(source.Ids[0]);
        int frozenCount = first.Length;
        clock.Advance();
        source.Queue(BatchSize, WindowCount, notify);
        input.Update();
        int retained = 0;
        int gaps = 0;
        int pressed = 0;
        bool originScopes = true;
        bool ordered = true;
        ulong prior = 0;
        ulong tail = 0;
        foreach (InputDeviceId id in source.Ids)
        {
            InputReadResult read = input.ReadRecords(id, 0);
            retained += read.Records.Length;
            ulong previousCapture = 0;
            foreach (InputRecord record in read.Records.Span)
            {
                if (record.Capture is InputCaptureStamp capture)
                {
                    ordered &= capture.Sequence > previousCapture;
                    previousCapture = capture.Sequence;
                }

                if (record.Data is KeyData)
                {
                    originScopes &= record.StateWindowId == source.Windows[0];
                }
            }

            prior = read.NextSequence;
            gaps += read.HasGap ? 1 : 0;
            tail = read.NextSequence;
            _ = input.GetRecords(id);
            _ = input.GetRecords(id);
            for (int w = 0; w < WindowCount; w++)
            {
                pressed += input.GetState(id, source.Windows[w]).IsDown(Key.A) ? 1 : 0;
            }

            input.ClearRecords(id);
        }

        return new(retained, gaps, notifications, pressed, frozenCount, first.Length, tail, originScopes, ordered, prior);
    }

    private sealed class WaveClock : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        public void Advance() => _ticks += TimeSpan.TicksPerMillisecond * 10;
    }

    private sealed class WaveSource(int count) : IOrderedInputSource
    {
        private InputEventBatch? _pending;
        private ulong _batch;
        private ulong _capture;

        public Guid StreamId { get; } = Guid.NewGuid();

        public InputDeviceId[] Ids { get; private set; } = [];

        public InputWindowId[] Windows { get; } = [new(Guid.NewGuid()), new(Guid.NewGuid())];

        public void Initialize(IInputDeviceRegistry registry) => Ids = Enumerable.Range(0, count).Select(_ => registry.RegisterDevice(new Keyboard())).ToArray();

        public void Queue(int size, int windows, bool cancel)
        {
            var events = new List<InputSourceEvent>();

            // Interleave devices in capture order, independently exercising each scope.
            for (int w = 0; w < windows; w++)
            {
                foreach (InputDeviceId id in Ids)
                {
                    events.Add(new(id, Windows[w], ++_capture, new FocusData(true)));
                }
            }

            for (int i = 0; i < size; i++)
            {
                foreach (InputDeviceId id in Ids)
                {
                    events.Add(new(id, Windows[i % windows], ++_capture, new KeyData(Key.A, true, i >= windows)));
                }
            }

            if (cancel)
            {
                for (int w = 0; w < windows; w++)
                {
                    foreach (InputDeviceId id in Ids)
                    {
                        events.Add(new(id, Windows[w], ++_capture, new FocusData(false)));
                    }
                }
            }

            _pending = new(++_batch, _capture, events);
        }

        public InputEventBatch? PeekEvents() => _pending;

        public void CommitEvents(ulong batchId)
        {
            if (_pending?.Id != batchId)
            {
                throw new InvalidOperationException();
            }

            _pending = null;
        }

        public void Update()
        {
        }

        public void Shutdown()
        {
        }

        public void AbortEvents() => _pending = null;

        public void Dispose()
        {
        }
    }

    private sealed class Keyboard : IInputDevice
    {
        public InputDeviceDescriptor Descriptor { get; } = new(InputDeviceKind.Keyboard, "synthetic keyboard", InputDeviceIdentityKind.Physical);

        public IReadOnlyList<InputData> DrainEvents() => [];

        public void Dispose()
        {
        }
    }
}
