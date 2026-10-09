namespace Lumyte.Diagnostics.Transport;

/// <summary>Creates authenticated connections independent of the physical protocol.</summary>
public interface IDiagnosticTransportFactory
{
    /// <summary>Connects and publishes the initial catalog.</summary>
    /// <param name="hello">The game identity and capabilities.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The established connection.</returns>
    ValueTask<IDiagnosticConnection> OpenAsync(ClientHello hello, CancellationToken cancellationToken);
}
