using Lumyte.Diagnostics.Transport;

namespace Lumyte.Diagnostics.Server;

internal sealed class DiagnosticDashboardReader(DiagnosticSessionRegistry sessions) : IDiagnosticDashboardReader
{
    public long Version => sessions.UiChanges.Version;

    public SessionSnapshot[] Resources() => sessions.List();

    public DiagnosticUiSession[] Summaries() => sessions.UiSessions();

    public DiagnosticEvent[] Events(Guid sessionId) => sessions.UiTelemetry(sessionId);

    public Task<long> WaitForChangeAsync(long version, CancellationToken cancellationToken) => sessions.UiChanges.WaitAsync(version, cancellationToken);
}
