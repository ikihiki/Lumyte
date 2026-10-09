using Lumyte.Diagnostics.Transport;

namespace Lumyte.Diagnostics.Server;

/// <summary>Detached dashboard queries and cancellable, coalesced change notifications.</summary>
internal interface IDiagnosticDashboardReader
{
    /// <summary>Gets the current change version.</summary>
    long Version { get; }

    /// <summary>Reads resource catalogs without connection credentials.</summary>
    /// <returns>Detached resource snapshots.</returns>
    SessionSnapshot[] Resources();

    /// <summary>Reads compact resource counters.</summary>
    /// <returns>Resource summaries without catalogs or telemetry.</returns>
    DiagnosticUiSession[] Summaries();

    /// <summary>Reads the latest bounded events for a resource.</summary>
    /// <param name="sessionId">The selected game session.</param>
    /// <returns>A detached tail, or an empty array after disconnection.</returns>
    DiagnosticEvent[] Events(Guid sessionId);

    /// <summary>Waits until state changes; multiple writes may share one notification.</summary>
    /// <param name="version">The version observed before reading state.</param>
    /// <param name="cancellationToken">The browser connection lifetime.</param>
    /// <returns>A newer version. No event history or operation replay is implied.</returns>
    Task<long> WaitForChangeAsync(long version, CancellationToken cancellationToken);
}
