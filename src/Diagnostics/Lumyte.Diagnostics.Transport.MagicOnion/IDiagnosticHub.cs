using MagicOnion;

namespace Lumyte.Diagnostics.Transport.MagicOnion;

/// <summary>A native MessagePack StreamingHub contract.</summary>
public interface IDiagnosticHub : IStreamingHub<IDiagnosticHub, IDiagnosticReceiver>
{
    /// <summary>Negotiates the initial session and catalogs.</summary>
    /// <param name="hello">The game hello.</param>
    /// <returns>The established session.</returns>
    Task<WireWelcome> JoinAsync(WireHello hello);

    /// <summary>Publishes a closed result or telemetry message.</summary>
    /// <param name="message">The message.</param>
    /// <returns>The receipt.</returns>
    Task<WireReceipt> PublishAsync(WireMessage message);
}
