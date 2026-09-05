namespace Lumyte.DevTools.Host;

public sealed class DiagnosticsDomain : IDisposable
{
    public static readonly DevToolsDomain Domain = new("diagnostics");
    public static readonly DevToolsQuery<DiagnosticsCatalogRequest, DiagnosticsCatalog> GetCatalog = new("getCatalog");
    public static readonly DevToolsQuery<DiagnosticsSnapshotRequest, DiagnosticsSnapshot> GetSnapshot = new("getSnapshot");
    public static readonly DevToolsQuery<DiagnosticsStatusRequest, DiagnosticsStatus> GetStatus = new("getStatus");
    public static readonly DevToolsCommand<DiagnosticsControlRequest, DiagnosticsStatus> Control = new("control");
    public static readonly DevToolsEvent<DiagnosticsBatchUpdate> Updated = new("updated");
    private readonly DiagnosticsCollector collector; private readonly DevToolsEventPublisher<DiagnosticsBatchUpdate> publisher; private readonly IDisposable[] registrations; private readonly Timer timer; private long publishedVersion; private int publishing;
    public DiagnosticsDomain(DevToolsHub hub, DiagnosticsCollector collector)
    {
        this.collector = collector;
        publisher = hub.RegisterEvent(Domain, Updated);
        registrations = [hub.RegisterQuery(Domain, GetCatalog, (request, token) => { token.ThrowIfCancellationRequested(); return ValueTask.FromResult(collector.GetCatalog()); }), hub.RegisterQuery(Domain, GetSnapshot, SnapshotAsync), hub.RegisterQuery(Domain, GetStatus, (request, token) => { token.ThrowIfCancellationRequested(); return ValueTask.FromResult(collector.GetStatus()); }), hub.RegisterCommand(Domain, Control, ControlAsync)];
        timer = new Timer(_ => PublishBatch(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }
    public void Dispose() { timer.Dispose(); foreach (IDisposable registration in registrations.Reverse()) { registration.Dispose(); } publisher.Dispose(); }
    private ValueTask<DiagnosticsSnapshot> SnapshotAsync(DiagnosticsSnapshotRequest request, CancellationToken token) { token.ThrowIfCancellationRequested(); return ValueTask.FromResult(collector.GetSnapshot(request.Source, request.Name, request.Status)); }
    private ValueTask<DiagnosticsStatus> ControlAsync(DiagnosticsControlRequest request, CancellationToken token) { token.ThrowIfCancellationRequested(); switch (request.Action.ToLowerInvariant()) { case "pause": collector.Pause(); break; case "resume": collector.Resume(); break; case "clear": collector.Clear(); break; default: throw new ArgumentException($"Unknown diagnostics action '{request.Action}'.", nameof(request)); } return ValueTask.FromResult(collector.GetStatus()); }
    private void PublishBatch() { long version = collector.Version; if (version == publishedVersion || Interlocked.Exchange(ref publishing, 1) != 0) { return; } try { publishedVersion = version; DiagnosticsSnapshot snapshot = collector.GetSnapshot(); publisher.PublishAsync(new(DateTimeOffset.UtcNow, snapshot.Status, [.. snapshot.Activities.Take(32)], snapshot.Metrics)).AsTask().GetAwaiter().GetResult(); } catch (ObjectDisposedException) { } finally { Volatile.Write(ref publishing, 0); } }
}
public sealed record DiagnosticsCatalogRequest;
public sealed record DiagnosticsSnapshotRequest(string? Source = null, string? Name = null, string? Status = null);
public sealed record DiagnosticsStatusRequest;
public sealed record DiagnosticsControlRequest(string Action);
public sealed record DiagnosticsBatchUpdate(DateTimeOffset Timestamp, DiagnosticsStatus Status, IReadOnlyList<DiagnosticActivity> Activities, IReadOnlyList<MetricSeriesSnapshot> Metrics);
