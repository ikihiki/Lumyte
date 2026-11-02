using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Reflection;
using Lumyte.Graphics.Abstractions;

internal sealed class DetailedDiagnostics : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Aggregate> _phases = new();
    private readonly PropertyInfo _callback;
    private readonly int[] _collections = [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)];
    private TimeSpan _pause = GC.GetTotalPauseDuration();

    internal DetailedDiagnostics()
    {
        Type type = typeof(IGraphicDevice).Assembly.GetType("Lumyte.Graphics.Abstractions.UiDeviceProbe")
            ?? throw new InvalidOperationException("Apply detail-instrumentation.patch before using --detail.");
        _callback = type.GetProperty("Completed")!;
        _callback.SetValue(null, (Action<string, long, long, long, int>)Add);
    }

    public void Dispose() => _callback.SetValue(null, null);

    internal Snapshot Capture()
    {
        int[] collections = [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)];
        TimeSpan pause = GC.GetTotalPauseDuration();
        var result = new Snapshot([], collections[0] - _collections[0], collections[1] - _collections[1], collections[2] - _collections[2], (pause - _pause).TotalMilliseconds);
        collections.CopyTo(_collections, 0);
        _pause = pause;
        lock (_gate)
        {
            result = result with { Phases = _phases.Where(p => p.Value.Calls != 0).Select(p => new Phase(p.Key, p.Value.Calls, p.Value.Ticks * 1000d / Stopwatch.Frequency, p.Value.Maximum * 1000d / Stopwatch.Frequency, p.Value.Bytes, p.Value.CrossThread, p.Value.First, p.Value.Last)).ToArray() };
            foreach (string name in _phases.Keys) _phases[name] = default;
        }
        return result;
    }

    internal void SourceUpdated(long started, long allocated)
        => Add("signal.notify", started, Stopwatch.GetTimestamp(), GC.GetAllocatedBytesForCurrentThread() - allocated, Environment.CurrentManagedThreadId);

    private void Add(string name, long started, long ended, long bytes, int thread)
    {
        lock (_gate)
        {
            Aggregate value = _phases.GetValueOrDefault(name);
            value.Calls++;
            value.First = value.First == 0 ? started : Math.Min(value.First, started);
            value.Last = Math.Max(value.Last, ended);
            value.Ticks += ended - started;
            value.Maximum = Math.Max(value.Maximum, ended - started);
            if (bytes >= 0) value.Bytes += bytes;
            else value.CrossThread++;
            _phases[name] = value;
        }
    }

    internal sealed record Snapshot(Phase[] Phases, int Gen0, int Gen1, int Gen2, double GcPauseMilliseconds);

    internal sealed record Phase(string Name, int Calls, double Milliseconds, double MaximumMilliseconds, long AllocatedBytes, int CrossThreadSpans, long StartedTimestamp, long EndedTimestamp);

    private struct Aggregate
    {
        internal int Calls;
        internal long Ticks;
        internal long Maximum;
        internal long Bytes;
        internal int CrossThread;
        internal long First;
        internal long Last;
    }
}

[EventSource(Name = "Lumyte-UI-Device-Benchmark")]
internal sealed class DeviceBenchmarkEvents : EventSource
{
    internal static readonly DeviceBenchmarkEvents Log = new();

    [Event(1)]
    public void Frame(long index) { if (IsEnabled()) WriteEvent(1, index); }
}
