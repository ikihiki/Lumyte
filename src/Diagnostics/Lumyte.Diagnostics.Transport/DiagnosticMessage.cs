namespace Lumyte.Diagnostics.Transport;

/// <summary>A closed message carrying a result, telemetry or heartbeat.</summary>
/// <param name="MessageId">The MessageId value.</param>
/// <param name="SessionId">The SessionId value.</param>
/// <param name="Kind">The Kind value.</param>
/// <param name="RequestId">The RequestId value.</param>
/// <param name="Result">The Result value.</param>
/// <param name="Events">The Events value.</param>
public sealed record DiagnosticMessage(Guid MessageId, Guid SessionId, DiagnosticMessageKind Kind, Guid? RequestId, DiagnosticOperationResult? Result, DiagnosticEvent[] Events);
