using System.Diagnostics;
using System.Diagnostics.Metrics;
using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Diagnostics.RenderGraph;

/// <summary>Collects fixed-size aggregate counters and explicitly requested, bounded graph captures.</summary>
/// <remarks>Use on the diagnostic pump's owner thread. Capture data never retains native resources.</remarks>
public sealed partial class RenderGraphDiagnostics : IRenderGraphObserver, IDisposable
{
    /// <summary>The meter, activity source and logger category name.</summary>
    public const string TelemetryName = "Lumyte.Graphics.RenderGraph";

    internal const long CaptureOverheadBytes = 2048;

    private static readonly Action<ILogger, string, string, string, long, string, Exception?> _failed = LoggerMessage.Define<string, string, string, long, string>(LogLevel.Error, new(3001, "RenderGraphFailed"), "Graph {Graph} ({GraphId}), run {RunId}, frame {FrameId}: {Operation} failed");
    private readonly ActivitySource _activities = new(TelemetryName);
    private readonly ILogger _logger;
    private readonly string _instanceId;
    private readonly KeyValuePair<string, object?> _instanceTag;
    private readonly RenderGraphDiagnosticOptions _options;
    private readonly TimeProvider _clock;
    private readonly bool _enabled;
    private readonly Counter<long> _operations;
    private readonly Counter<long> _counts;
    private readonly Counter<long> _failures;
    private readonly Counter<double> _cpuTotal;
    private readonly Histogram<double> _cpuMaximum;
    private readonly Dictionary<Guid, CaptureEntry> _captures = [];
    private readonly LinkedList<CaptureEntry> _order = [];
    private CaptureEntry? _pending;
    private Accumulator _totals;
    private Accumulator _window;
    private long _windowStarted;
    private long _retainedBytes;
    private long _lastFailureLog;
    private bool _hasFailureLog;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="RenderGraphDiagnostics"/> class.</summary>
    /// <param name="meters">The diagnostic session's meter factory.</param>
    /// <param name="loggers">The existing logging factory.</param>
    /// <param name="identity">The current game execution identity.</param>
    /// <param name="diagnostics">The existing diagnostic collection configuration.</param>
    /// <param name="options">The capture budgets and aggregation interval.</param>
    /// <param name="clock">The clock used for publication intervals and capture expiry.</param>
    public RenderGraphDiagnostics(IMeterFactory meters, ILoggerFactory loggers, IGameExecutionIdentity identity, DiagnosticOptions diagnostics, RenderGraphDiagnosticOptions options, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(meters);
        ArgumentNullException.ThrowIfNull(loggers);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        options.Validate();
        _options = new RenderGraphDiagnosticOptions
        {
            Enabled = options.Enabled,
            MetricInterval = options.MetricInterval,
            MaxCaptures = options.MaxCaptures,
            MaxSnapshotBytes = options.MaxSnapshotBytes,
            MaxRetainedBytes = options.MaxRetainedBytes,
            Lifetime = options.Lifetime,
        };
        _clock = clock;
        _enabled = diagnostics.Enabled && options.Enabled;
        _windowStarted = clock.GetTimestamp();
        Meter meter = meters.Create(new MeterOptions(TelemetryName));
        _operations = meter.CreateCounter<long>("rendergraph.operations", description: "Successful compile and record operations, published in bounded-frequency windows.");
        _counts = meter.CreateCounter<long>("rendergraph.plan.total", description: "Sums of compiled plan counts per publication window.");
        _failures = meter.CreateCounter<long>("rendergraph.failures", description: "Exact failure counts per publication window.");
        _cpuTotal = meter.CreateCounter<double>("rendergraph.cpu.total", "ms", "Sum of successful CPU operation durations per publication window; not GPU time.");
        _cpuMaximum = meter.CreateHistogram<double>("rendergraph.cpu.window.max", "ms", "Maximum successful CPU duration in each publication window; not individual operation durations.");
        _logger = loggers.CreateLogger(TelemetryName);
        _instanceId = identity.InstanceId.ToString("D");
        _instanceTag = new("lumyte.instance.id", _instanceId);
    }

    /// <summary>Gets a value indicating whether both diagnostic configurations enable this observer.</summary>
    public bool IsEnabled => _enabled && !_disposed;

    /// <summary>Binds one pending capture to the next matching compilation.</summary>
    /// <param name="run">The graph execution about to compile.</param>
    /// <returns>The bounded capture request, or null for ordinary instrumentation.</returns>
    public RenderGraphCaptureOptions? RequestCapture(RenderGraphRun run)
    {
        if (!IsEnabled)
        {
            return null;
        }

        Expire();
        CaptureEntry? entry = _pending;
        if (entry is null || (entry.Filter.Length != 0 && entry.Filter != run.Name))
        {
            return null;
        }

        _pending = null;
        entry.Run = run;
        entry.HasRun = true;
        entry.State = "capturing";
        long maximum = Math.Min(_options.MaxSnapshotBytes, _options.MaxRetainedBytes - CaptureOverheadBytes);
        MakeRoom(CaptureOverheadBytes + maximum - entry.Bytes, entry);
        SetBytes(entry, CaptureOverheadBytes + maximum);
        return new() { MaxBytes = maximum };
    }

    /// <summary>Starts a trace only for a requested capture when activity listeners are present.</summary>
    /// <param name="run">The compilation or recording identity.</param>
    /// <param name="operation">The CPU operation.</param>
    /// <returns>A sampled activity, or null without per-operation scope allocation.</returns>
    public IDisposable? Begin(RenderGraphRun run, string operation)
    {
        if (!IsEnabled || !_activities.HasListeners())
        {
            return null;
        }

        Expire();
        if (FindRun(run) is null)
        {
            return null;
        }

        return _activities.StartActivity(operation, ActivityKind.Internal, default(ActivityContext), [_instanceTag, new("graph.id", run.GraphId.ToString("D")), new("graph.run.id", run.RunId.ToString("D")), new("graph.name", run.Name), new("frame.id", run.FrameId)]);
    }

    /// <summary>Updates scalar aggregates and completes only the capture bound to this run.</summary>
    /// <param name="run">The completed compilation.</param>
    /// <param name="statistics">The fixed-size compiled plan counts.</param>
    /// <param name="snapshot">The requested details, or null when absent or over budget.</param>
    /// <param name="durationMilliseconds">The successful compile CPU duration.</param>
    public void Compiled(RenderGraphRun run, RenderGraphStatistics statistics, RenderGraphSnapshot? snapshot, double durationMilliseconds)
    {
        if (!IsEnabled)
        {
            return;
        }

        Expire();
        _totals.AddCompile(statistics, durationMilliseconds);
        _window.AddCompile(statistics, durationMilliseconds);
        CaptureEntry? entry = FindRun(run);
        if (entry is not null)
        {
            entry.Statistics = statistics;
            entry.CompileMilliseconds = durationMilliseconds;
            entry.Snapshot = snapshot;
            entry.State = snapshot is null ? "too-large" : "compiled";
            SetBytes(entry, CaptureOverheadBytes + (snapshot?.EstimatedBytes ?? 0));
        }

        PublishIfDue();
    }

    /// <summary>Records CPU completion for this run without implying GPU completion.</summary>
    /// <param name="run">The successfully recorded compilation.</param>
    /// <param name="durationMilliseconds">The successful record CPU duration.</param>
    public void Recorded(RenderGraphRun run, double durationMilliseconds)
    {
        if (!IsEnabled)
        {
            return;
        }

        Expire();
        _totals.AddRecord(durationMilliseconds);
        _window.AddRecord(durationMilliseconds);
        CaptureEntry? entry = FindRun(run);
        if (entry is not null)
        {
            entry.RecordMilliseconds = durationMilliseconds;
            if (entry.State != "too-large")
            {
                entry.State = "recorded";
            }
        }

        PublishIfDue();
    }

    /// <summary>Counts every failure and reports at most one structured error log per interval.</summary>
    /// <param name="run">The failed graph execution.</param>
    /// <param name="operation">The failed CPU operation.</param>
    /// <param name="exception">The original failure.</param>
    public void Failed(RenderGraphRun run, string operation, Exception exception)
    {
        if (!IsEnabled)
        {
            return;
        }

        Expire();
        _totals.Failures++;
        _window.Failures++;
        CaptureEntry? entry = FindRun(run);
        if (entry is not null)
        {
            entry.State = operation + "-failed";
            if (entry.Snapshot is null)
            {
                SetBytes(entry, CaptureOverheadBytes);
            }
        }

        Activity? activity = Activity.Current;
        if (activity?.Source == _activities
            && activity.GetTagItem("graph.run.id") as string == run.RunId.ToString("D")
            && activity.GetTagItem("graph.id") as string == run.GraphId.ToString("D"))
        {
            activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        }

        long now = _clock.GetTimestamp();
        if ((!_hasFailureLog || _clock.GetElapsedTime(_lastFailureLog, now) >= _options.MetricInterval) && _logger.IsEnabled(LogLevel.Error))
        {
            _hasFailureLog = true;
            _lastFailureLog = now;
            using IDisposable? logging = _logger.BeginScope(new Dictionary<string, object?> { ["lumyte.instance.id"] = _instanceId });
            _failed(_logger, run.Name, run.GraphId.ToString("D"), run.RunId.ToString("D"), run.FrameId, operation, exception);
        }

        PublishIfDue();
    }

    /// <summary>Publishes the final partial metric window and releases captures and the activity source.</summary>
    /// <remarks>The final publication can occur before the regular interval; the factory owns meter lifetime.</remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        PublishWindow();
        _disposed = true;
        _captures.Clear();
        _order.Clear();
        _pending = null;
        _retainedBytes = 0;
        _activities.Dispose();
    }

    private static DiagnosticResult<T> MissingDetails<T>(CaptureEntry? entry)
        where T : notnull
        => DiagnosticResult<T>.Reject(entry is null ? "not-found" : entry.State == "too-large" ? "too-large" : "not-ready", "Capture details are unavailable.");

    [DiagnosticOperation(DiagnosticPermission.Observe)]
    private DiagnosticResult<RenderGraphSummary> GetSummary()
    {
        Expire();
        PublishIfDue();
        return DiagnosticResult<RenderGraphSummary>.Success(new(_totals.Compiles, _totals.Records, _totals.Failures, _totals.CompileSum, _totals.CompileMaximum, _totals.RecordSum, _totals.RecordMaximum, _totals.LivePasses, _totals.CulledPasses, _totals.Barriers, _captures.Count, _pending is not null, _retainedBytes));
    }

    [DiagnosticOperation(DiagnosticPermission.Observe, Id = "request-capture", DisplayName = "Request capture")]
    private DiagnosticResult<RenderGraphCaptureRequest> CaptureNext([DiagnosticArgument(MaxLength = 256)] string graphName)
    {
        if (!IsEnabled)
        {
            return DiagnosticResult<RenderGraphCaptureRequest>.Reject("disabled", "Render graph diagnostics are disabled.");
        }

        Expire();
        if (_pending is not null)
        {
            return DiagnosticResult<RenderGraphCaptureRequest>.Reject("busy", "A graph capture is already pending.");
        }

        while (_captures.Count >= _options.MaxCaptures)
        {
            Remove(_order.First!.Value);
        }

        MakeRoom(CaptureOverheadBytes, null);
        var entry = new CaptureEntry(Guid.NewGuid(), graphName, _clock.GetTimestamp());
        entry.Node = _order.AddLast(entry);
        _captures.Add(entry.Id, entry);
        _retainedBytes += entry.Bytes;
        _pending = entry;
        return DiagnosticResult<RenderGraphCaptureRequest>.Success(new(entry.Id.ToString("D"), entry.State));
    }

    [DiagnosticOperation(DiagnosticPermission.Observe)]
    private DiagnosticResult<RenderGraphCaptureDiagnostic> GetCapture([DiagnosticArgument(MaxLength = 36)] string captureId)
    {
        CaptureEntry? entry = FindCapture(captureId);
        if (entry is null)
        {
            return DiagnosticResult<RenderGraphCaptureDiagnostic>.Reject("not-found", "Capture was not found, expired or evicted.");
        }

        RenderGraphStatistics counts = entry.Statistics;
        return DiagnosticResult<RenderGraphCaptureDiagnostic>.Success(new(entry.Id.ToString("D"), entry.HasRun ? entry.Run.GraphId.ToString("D") : string.Empty, entry.HasRun ? entry.Run.RunId.ToString("D") : string.Empty, entry.HasRun ? entry.Run.Name : entry.Filter, entry.HasRun ? entry.Run.FrameId : -1, entry.State, counts.LivePasses, counts.CulledPasses, counts.Resources, counts.TransientResources, counts.Dependencies, counts.Barriers, entry.CompileMilliseconds, entry.RecordMilliseconds, entry.Bytes));
    }

    [DiagnosticOperation(DiagnosticPermission.Observe)]
    private DiagnosticResult<RenderGraphPassDiagnostic> GetPass([DiagnosticArgument(MaxLength = 36)] string captureId, [DiagnosticArgument(Minimum = 0)] long index)
    {
        CaptureEntry? entry = FindCapture(captureId);
        if (entry?.Snapshot is not RenderGraphSnapshot snapshot)
        {
            return MissingDetails<RenderGraphPassDiagnostic>(entry);
        }

        if (index >= snapshot.Passes.Count)
        {
            return DiagnosticResult<RenderGraphPassDiagnostic>.Reject("not-found", "Pass index is outside the capture.");
        }

        RenderGraphPassInfo pass = snapshot.Passes[(int)index];
        return DiagnosticResult<RenderGraphPassDiagnostic>.Success(new(pass.Name, pass.ExecutionIndex, pass.HasSideEffects));
    }

    [DiagnosticOperation(DiagnosticPermission.Observe)]
    private DiagnosticResult<RenderGraphResourceDiagnostic> GetResource([DiagnosticArgument(MaxLength = 36)] string captureId, [DiagnosticArgument(Minimum = 0)] long index)
    {
        CaptureEntry? entry = FindCapture(captureId);
        if (entry?.Snapshot is not RenderGraphSnapshot snapshot)
        {
            return MissingDetails<RenderGraphResourceDiagnostic>(entry);
        }

        if (index >= snapshot.Resources.Count)
        {
            return DiagnosticResult<RenderGraphResourceDiagnostic>.Reject("not-found", "Resource index is outside the capture.");
        }

        RenderGraphResourceInfo resource = snapshot.Resources[(int)index];
        return DiagnosticResult<RenderGraphResourceDiagnostic>.Success(new(resource.Name, resource.Kind, resource.Transient, resource.Output, resource.FirstPass, resource.LastPass));
    }

    [DiagnosticOperation(DiagnosticPermission.Observe)]
    private DiagnosticResult<RenderGraphDependencyDiagnostic> GetDependency([DiagnosticArgument(MaxLength = 36)] string captureId, [DiagnosticArgument(Minimum = 0)] long index)
    {
        CaptureEntry? entry = FindCapture(captureId);
        if (entry?.Snapshot is not RenderGraphSnapshot snapshot)
        {
            return MissingDetails<RenderGraphDependencyDiagnostic>(entry);
        }

        if (index >= snapshot.Dependencies.Count)
        {
            return DiagnosticResult<RenderGraphDependencyDiagnostic>.Reject("not-found", "Dependency index is outside the capture.");
        }

        RenderGraphDependencyInfo dependency = snapshot.Dependencies[(int)index];
        return DiagnosticResult<RenderGraphDependencyDiagnostic>.Success(new(dependency.Before, dependency.After));
    }

    [DiagnosticOperation(DiagnosticPermission.Observe)]
    private DiagnosticResult<RenderGraphBarrierDiagnostic> GetBarrier([DiagnosticArgument(MaxLength = 36)] string captureId, [DiagnosticArgument(Minimum = 0)] long index)
    {
        CaptureEntry? entry = FindCapture(captureId);
        if (entry?.Snapshot is not RenderGraphSnapshot snapshot)
        {
            return MissingDetails<RenderGraphBarrierDiagnostic>(entry);
        }

        if (index >= snapshot.Barriers.Count)
        {
            return DiagnosticResult<RenderGraphBarrierDiagnostic>.Reject("not-found", "Barrier index is outside the capture.");
        }

        RenderGraphBarrierInfo barrier = snapshot.Barriers[(int)index];
        return DiagnosticResult<RenderGraphBarrierDiagnostic>.Success(new(barrier.PassIndex, barrier.ResourceIndex, barrier.BeforeState.ToString(), barrier.AfterState.ToString(), (long)barrier.Before.Stages, (long)barrier.Before.Access, (long)barrier.After.Stages, (long)barrier.After.Access));
    }

    private CaptureEntry? FindCapture(string id)
    {
        Expire();
        return Guid.TryParseExact(id, "D", out Guid parsed) ? _captures.GetValueOrDefault(parsed) : null;
    }

    private CaptureEntry? FindRun(RenderGraphRun run)
    {
        for (LinkedListNode<CaptureEntry>? node = _order.First; node is not null; node = node.Next)
        {
            if (node.Value.HasRun && node.Value.Run.RunId == run.RunId && node.Value.Run.GraphId == run.GraphId)
            {
                return node.Value;
            }
        }

        return null;
    }

    private void Expire()
    {
        long now = _clock.GetTimestamp();
        while (_order.First is { Value: CaptureEntry entry } && _clock.GetElapsedTime(entry.CreatedAt, now) >= _options.Lifetime)
        {
            Remove(entry);
        }
    }

    private void MakeRoom(long additionalBytes, CaptureEntry? keep)
    {
        while (additionalBytes > _options.MaxRetainedBytes - _retainedBytes)
        {
            LinkedListNode<CaptureEntry>? oldest = _order.First;
            if (oldest is not null && oldest.Value == keep)
            {
                oldest = oldest.Next;
            }

            if (oldest is null)
            {
                throw new InvalidOperationException("Capture limits cannot fit the reserved record.");
            }

            Remove(oldest.Value);
        }
    }

    private void Remove(CaptureEntry entry)
    {
        _captures.Remove(entry.Id);
        _order.Remove(entry.Node!);
        _retainedBytes -= entry.Bytes;
        if (_pending == entry)
        {
            _pending = null;
        }
    }

    private void SetBytes(CaptureEntry entry, long bytes)
    {
        _retainedBytes += bytes - entry.Bytes;
        entry.Bytes = bytes;
    }

    private void PublishIfDue()
    {
        long now = _clock.GetTimestamp();
        if (!IsEnabled || _clock.GetElapsedTime(_windowStarted, now) < _options.MetricInterval)
        {
            return;
        }

        PublishWindow();
    }

    private void PublishWindow()
    {
        if (!IsEnabled)
        {
            return;
        }

        if (_window.Compiles > 0)
        {
            PublishOperation("compile", _window.Compiles, _window.CompileSum, _window.CompileMaximum);
            Count("passes.live", _window.LivePasses);
            Count("passes.culled", _window.CulledPasses);
            Count("resources", _window.Resources);
            Count("resources.transient", _window.TransientResources);
            Count("dependencies", _window.Dependencies);
            Count("barriers", _window.Barriers);
        }

        if (_window.Records > 0)
        {
            PublishOperation("record", _window.Records, _window.RecordSum, _window.RecordMaximum);
        }

        if (_window.Failures > 0)
        {
            _failures.Add(_window.Failures, _instanceTag);
        }

        _window = default;
        _windowStarted = _clock.GetTimestamp();
    }

    private void PublishOperation(string operation, long count, double total, double maximum)
    {
        var tag = new KeyValuePair<string, object?>("operation", operation);
        _operations.Add(count, _instanceTag, tag);
        _cpuTotal.Add(total, _instanceTag, tag);
        _cpuMaximum.Record(maximum, _instanceTag, tag);
    }

    private void Count(string kind, long value)
        => _counts.Add(value, _instanceTag, new KeyValuePair<string, object?>("kind", kind));

    private struct Accumulator
    {
        public long Compiles { get; set; }

        public long Records { get; set; }

        public long Failures { get; set; }

        public double CompileSum { get; set; }

        public double CompileMaximum { get; set; }

        public double RecordSum { get; set; }

        public double RecordMaximum { get; set; }

        public long LivePasses { get; set; }

        public long CulledPasses { get; set; }

        public long Resources { get; set; }

        public long TransientResources { get; set; }

        public long Dependencies { get; set; }

        public long Barriers { get; set; }

        public void AddCompile(RenderGraphStatistics statistics, double duration)
        {
            Compiles++;
            CompileSum += duration;
            CompileMaximum = Math.Max(CompileMaximum, duration);
            LivePasses += statistics.LivePasses;
            CulledPasses += statistics.CulledPasses;
            Resources += statistics.Resources;
            TransientResources += statistics.TransientResources;
            Dependencies += statistics.Dependencies;
            Barriers += statistics.Barriers;
        }

        public void AddRecord(double duration)
        {
            Records++;
            RecordSum += duration;
            RecordMaximum = Math.Max(RecordMaximum, duration);
        }
    }

    private sealed class CaptureEntry(Guid id, string filter, long createdAt)
    {
        public Guid Id { get; } = id;

        public string Filter { get; } = filter;

        public long CreatedAt { get; } = createdAt;

        public LinkedListNode<CaptureEntry>? Node { get; set; }

        public RenderGraphRun Run { get; set; }

        public bool HasRun { get; set; }

        public string State { get; set; } = "pending";

        public RenderGraphStatistics Statistics { get; set; }

        public RenderGraphSnapshot? Snapshot { get; set; }

        public double CompileMilliseconds { get; set; }

        public double RecordMilliseconds { get; set; }

        public long Bytes { get; set; } = CaptureOverheadBytes;
    }
}
