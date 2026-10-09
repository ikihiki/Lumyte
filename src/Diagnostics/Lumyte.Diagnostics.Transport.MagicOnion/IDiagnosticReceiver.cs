namespace Lumyte.Diagnostics.Transport.MagicOnion;

/// <summary>The pushed server command callback.</summary>
public interface IDiagnosticReceiver
{
    /// <summary>Delivers one server-authenticated command.</summary>
    /// <param name="command">The command.</param>
    void OnCommand(WireCommand command);
}
