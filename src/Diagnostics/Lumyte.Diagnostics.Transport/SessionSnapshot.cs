namespace Lumyte.Diagnostics.Transport;

/// <summary>The operator-visible session state, excluding credentials.</summary>
/// <param name="SessionId">The SessionId value.</param>
/// <param name="InstanceId">The InstanceId value.</param>
/// <param name="Catalog">The Catalog value.</param>
/// <param name="PendingCommands">The PendingCommands value.</param>
/// <param name="TelemetryReceived">The TelemetryReceived value.</param>
/// <param name="TelemetryDropped">The TelemetryDropped value.</param>
public sealed record SessionSnapshot(Guid SessionId, Guid InstanceId, DiagnosticSubsystemCatalog[] Catalog, int PendingCommands, long TelemetryReceived, long TelemetryDropped);
