namespace Lumyte.Diagnostics.Transport;

/// <summary>The supported initial message payloads.</summary>
public enum DiagnosticMessageKind
{
    /// <summary>Completed operation.</summary>
    CommandResult,

    /// <summary>Detached standard telemetry events.</summary>
    Telemetry,

    /// <summary>Session liveness.</summary>
    Heartbeat,
}
