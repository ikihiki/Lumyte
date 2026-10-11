using System.Diagnostics;
using System.Diagnostics.Metrics;
using Lumyte.Graphics.RenderGraph;
using Microsoft.Extensions.Logging;

namespace Lumyte.Diagnostics.RenderGraph;

/// <summary>Exposes the latest detached graph through existing diagnostics and correlated standard telemetry.</summary>
/// <remarks>Use on the diagnostic pump's owner thread. Retains one snapshot and no native resources.</remarks>
public sealed partial class RenderGraphDiagnostics : IRenderGraphObserver, IDisposable
{
    /// <summary>The meter, activity source and logger category name.</summary>
    public const string TelemetryName = "Lumyte.Graphics.RenderGraph";

    private static readonly Action<ILogger, string, int, int, Exception?> _compiled = LoggerMessage.Define<string, int, int>(LogLevel.Debug, new(3000, "RenderGraphCompiled"), "Graph {Graph} compiled: {LivePasses} live, {CulledPasses} culled");
    private static readonly Action<ILogger, string, Exception?> _failed = LoggerMessage.Define<string>(LogLevel.Error, new(3001, "RenderGraphFailed"), "Render graph {Operation} failed");
    private readonly ActivitySource _activities = new(TelemetryName);
    private readonly ILogger _logger;
    private readonly string _instanceId;
    private readonly Histogram<double> _duration;
    private readonly Histogram<long> _counts;
    private readonly Counter<long> _failures;
    private RenderGraphSnapshot? _snapshot;
    private long _revision;
    private double _compileMilliseconds;
    private double _recordMilliseconds;
    private string _status = "empty";

    /// <summary>Initializes a new instance of the <see cref="RenderGraphDiagnostics"/> class.</summary>
    /// <param name="meters">The diagnostic session's meter factory.</param>
    /// <param name="loggers">The existing logging factory.</param>
    /// <param name="identity">The current game execution identity.</param>
    public RenderGraphDiagnostics(IMeterFactory meters, ILoggerFactory loggers, IGameExecutionIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(meters);
        ArgumentNullException.ThrowIfNull(loggers);
        ArgumentNullException.ThrowIfNull(identity);
        Meter meter = meters.Create(new MeterOptions(TelemetryName));
        _duration = meter.CreateHistogram<double>("rendergraph.cpu.duration", "ms", "Compile and command recording CPU duration; not GPU time.");
        _counts = meter.CreateHistogram<long>("rendergraph.plan.count", description: "Live passes, culled passes, temporary allocations and barriers per compiled plan.");
        _failures = meter.CreateCounter<long>("rendergraph.failures", description: "Failed compile or record operations.");
        _logger = loggers.CreateLogger(TelemetryName);
        _instanceId = identity.InstanceId.ToString("D");
    }

    /// <summary>Starts an instance-correlated trace and logging scope.</summary>
    /// <param name="operation">The CPU operation.</param>
    /// <returns>A scope restored when the operation ends.</returns>
    public IDisposable? Begin(string operation)
    {
        IDisposable? logging = _logger.BeginScope(new Dictionary<string, object?> { ["lumyte.instance.id"] = _instanceId });
        Activity? activity = _activities.StartActivity(operation, ActivityKind.Internal, default(ActivityContext), [new("lumyte.instance.id", _instanceId)]);
        return new Correlation(activity, logging);
    }

    /// <summary>Publishes a detached compile snapshot and CPU measurements.</summary>
    /// <param name="snapshot">The compiled graph.</param>
    /// <param name="durationMilliseconds">The compile CPU duration.</param>
    public void Compiled(RenderGraphSnapshot snapshot, double durationMilliseconds)
    {
        _snapshot = snapshot;
        _compileMilliseconds = durationMilliseconds;
        _recordMilliseconds = 0;
        _status = "compiled";
        _revision++;
        int live = snapshot.Passes.Count(pass => pass.ExecutionIndex >= 0);
        int culled = snapshot.Passes.Count - live;
        RecordDuration("compile", durationMilliseconds);
        Count("passes.live", live);
        Count("passes.culled", culled);
        Count("resources.transient", snapshot.Resources.Count(resource => resource.Transient && resource.FirstPass >= 0));
        Count("barriers", snapshot.Barriers.Count);
        _compiled(_logger, snapshot.Name, live, culled, null);
    }

    /// <summary>Publishes recording completion without implying GPU completion.</summary>
    /// <param name="snapshot">The recorded graph.</param>
    /// <param name="durationMilliseconds">The record CPU duration.</param>
    public void Recorded(RenderGraphSnapshot snapshot, double durationMilliseconds)
    {
        if (_snapshot != snapshot)
        {
            _compileMilliseconds = 0;
        }

        _snapshot = snapshot;
        _recordMilliseconds = durationMilliseconds;
        _status = "recorded";
        _revision++;
        RecordDuration("record", durationMilliseconds);
    }

    /// <summary>Reports a failure through the existing structured log and metric collectors.</summary>
    /// <param name="operation">The failed CPU operation.</param>
    /// <param name="exception">The original failure.</param>
    public void Failed(string operation, Exception exception)
    {
        _status = operation + "-failed";
        _revision++;
        _failures.Add(1, new KeyValuePair<string, object?>("lumyte.instance.id", _instanceId), new KeyValuePair<string, object?>("operation", operation));
        if (Activity.Current?.Source == _activities)
        {
            Activity.Current.SetStatus(ActivityStatusCode.Error, exception.Message);
        }

        _failed(_logger, operation, exception);
    }

    /// <summary>Disposes the observer's activity source; meter lifetime belongs to the factory.</summary>
    public void Dispose() => _activities.Dispose();

    [DiagnosticOperation(DiagnosticPermission.Observe)]
    private DiagnosticResult<RenderGraphSummary> GetSummary()
    {
        if (_snapshot is null)
        {
            return DiagnosticResult<RenderGraphSummary>.Reject("not-ready", "No render graph has compiled successfully.");
        }

        int live = _snapshot.Passes.Count(pass => pass.ExecutionIndex >= 0);
        return DiagnosticResult<RenderGraphSummary>.Success(new(_snapshot.Name, _status, live, _snapshot.Passes.Count - live, _snapshot.Resources.Count, _snapshot.Dependencies.Count, _snapshot.Barriers.Count, _compileMilliseconds, _recordMilliseconds), _revision);
    }

    [DiagnosticOperation(DiagnosticPermission.Observe, RequiresRevision = true)]
    private DiagnosticResult<RenderGraphPassDiagnostic> GetPass(DiagnosticOperationContext context, [DiagnosticArgument(Minimum = 0)] long index)
    {
        if (context.ExpectedRevision != _revision)
        {
            return DiagnosticResult<RenderGraphPassDiagnostic>.Conflict(_revision);
        }

        if (_snapshot is null || index >= _snapshot.Passes.Count)
        {
            return DiagnosticResult<RenderGraphPassDiagnostic>.Reject("not-found", "Pass index is outside the latest snapshot.");
        }

        RenderGraphPassInfo pass = _snapshot.Passes[(int)index];
        return DiagnosticResult<RenderGraphPassDiagnostic>.Success(new(pass.Name, pass.ExecutionIndex, pass.HasSideEffects), _revision);
    }

    [DiagnosticOperation(DiagnosticPermission.Observe, RequiresRevision = true)]
    private DiagnosticResult<RenderGraphResourceDiagnostic> GetResource(DiagnosticOperationContext context, [DiagnosticArgument(Minimum = 0)] long index)
    {
        if (context.ExpectedRevision != _revision)
        {
            return DiagnosticResult<RenderGraphResourceDiagnostic>.Conflict(_revision);
        }

        if (_snapshot is null || index >= _snapshot.Resources.Count)
        {
            return DiagnosticResult<RenderGraphResourceDiagnostic>.Reject("not-found", "Resource index is outside the latest snapshot.");
        }

        RenderGraphResourceInfo resource = _snapshot.Resources[(int)index];
        return DiagnosticResult<RenderGraphResourceDiagnostic>.Success(new(resource.Name, resource.Kind, resource.Transient, resource.Output, resource.FirstPass, resource.LastPass), _revision);
    }

    [DiagnosticOperation(DiagnosticPermission.Observe, RequiresRevision = true)]
    private DiagnosticResult<RenderGraphDependencyDiagnostic> GetDependency(DiagnosticOperationContext context, [DiagnosticArgument(Minimum = 0)] long index)
    {
        if (context.ExpectedRevision != _revision)
        {
            return DiagnosticResult<RenderGraphDependencyDiagnostic>.Conflict(_revision);
        }

        if (_snapshot is null || index >= _snapshot.Dependencies.Count)
        {
            return DiagnosticResult<RenderGraphDependencyDiagnostic>.Reject("not-found", "Dependency index is outside the latest snapshot.");
        }

        RenderGraphDependencyInfo dependency = _snapshot.Dependencies[(int)index];
        return DiagnosticResult<RenderGraphDependencyDiagnostic>.Success(new(dependency.Before, dependency.After), _revision);
    }

    [DiagnosticOperation(DiagnosticPermission.Observe, RequiresRevision = true)]
    private DiagnosticResult<RenderGraphBarrierDiagnostic> GetBarrier(DiagnosticOperationContext context, [DiagnosticArgument(Minimum = 0)] long index)
    {
        if (context.ExpectedRevision != _revision)
        {
            return DiagnosticResult<RenderGraphBarrierDiagnostic>.Conflict(_revision);
        }

        if (_snapshot is null || index >= _snapshot.Barriers.Count)
        {
            return DiagnosticResult<RenderGraphBarrierDiagnostic>.Reject("not-found", "Barrier index is outside the latest snapshot.");
        }

        RenderGraphBarrierInfo barrier = _snapshot.Barriers[(int)index];
        return DiagnosticResult<RenderGraphBarrierDiagnostic>.Success(new(barrier.PassIndex, barrier.ResourceIndex, barrier.BeforeState.ToString(), barrier.AfterState.ToString(), (long)barrier.Before.Stages, (long)barrier.Before.Access, (long)barrier.After.Stages, (long)barrier.After.Access), _revision);
    }

    private void RecordDuration(string operation, double value)
        => _duration.Record(value, new KeyValuePair<string, object?>("lumyte.instance.id", _instanceId), new KeyValuePair<string, object?>("operation", operation));

    private void Count(string kind, long count)
        => _counts.Record(count, new KeyValuePair<string, object?>("lumyte.instance.id", _instanceId), new KeyValuePair<string, object?>("kind", kind));

    private sealed class Correlation(Activity? activity, IDisposable? logging) : IDisposable
    {
        public void Dispose()
        {
            activity?.Dispose();
            logging?.Dispose();
        }
    }
}
