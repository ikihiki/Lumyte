using Grpc.Core;
using Lumyte.Diagnostics.Transport.MagicOnion;
using MagicOnion;
using MagicOnion.Server.Hubs;

namespace Lumyte.Diagnostics.Server;

/// <summary>Authenticated native MessagePack command push and event ingestion.</summary>
/// <param name="sessions">The shared session coordinator.</param>
public sealed class DiagnosticHub(DiagnosticSessionRegistry sessions) : StreamingHubBase<IDiagnosticHub, IDiagnosticReceiver>, IDiagnosticHub
{
    private Guid? _session;

    /// <inheritdoc/>
    public Task<WireWelcome> JoinAsync(WireHello hello)
    {
        if (_session != null)
        {
            throw new ReturnStatusException(StatusCode.FailedPrecondition, "Already joined.");
        }

        try
        {
            Transport.SessionWelcome welcome = sessions.Open(WireMapper.FromWire(hello), command => Client.OnCommand(WireMapper.ToWire(command)));
            _session = welcome.SessionId;
            return Task.FromResult(WireMapper.ToWire(welcome));
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            throw new ReturnStatusException(StatusCode.InvalidArgument, "Invalid game hello.");
        }
    }

    /// <inheritdoc/>
    public Task<WireReceipt> PublishAsync(WireMessage message)
    {
        if (_session == null)
        {
            throw new ReturnStatusException(StatusCode.FailedPrecondition, "Join first.");
        }

        try
        {
            return Task.FromResult(WireMapper.ToWire(sessions.Publish(_session.Value, WireMapper.FromWire(message))));
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or UnauthorizedAccessException or KeyNotFoundException)
        {
            throw new ReturnStatusException(StatusCode.InvalidArgument, "Invalid diagnostic message or closed session.");
        }
    }

    /// <inheritdoc/>
    protected override ValueTask OnDisconnected()
    {
        if (_session is Guid id)
        {
            sessions.Close(id);
        }

        return ValueTask.CompletedTask;
    }
}
