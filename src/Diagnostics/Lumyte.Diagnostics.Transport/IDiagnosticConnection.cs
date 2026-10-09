namespace Lumyte.Diagnostics.Transport;

/// <summary>An owned, transport-independent diagnostic session.</summary>
public interface IDiagnosticConnection : IAsyncDisposable
{
    /// <summary>Gets the established session.</summary>
    SessionWelcome Welcome { get; }

    /// <summary>Reads commands using one reader per connection.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The server-authenticated commands.</returns>
    IAsyncEnumerable<DiagnosticCommand> ReadCommandsAsync(CancellationToken cancellationToken);

    /// <summary>Publishes a result or bounded telemetry batch.</summary>
    /// <param name="message">The message.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The server receipt.</returns>
    ValueTask<PublishReceipt> PublishAsync(DiagnosticMessage message, CancellationToken cancellationToken);
}
