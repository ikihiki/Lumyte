namespace Lumyte.Diagnostics.Server;

internal sealed record DiagnosticUiSession(Guid SessionId, Guid InstanceId, int PendingCommands, long TelemetryReceived, long TelemetryDropped);
