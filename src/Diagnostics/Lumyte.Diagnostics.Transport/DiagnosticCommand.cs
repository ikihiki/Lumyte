namespace Lumyte.Diagnostics.Transport;

/// <summary>A server-authenticated invocation with a UTC delivery deadline.</summary>
/// <param name="RequestId">The RequestId value.</param>
/// <param name="SubsystemId">The SubsystemId value.</param>
/// <param name="OperationId">The OperationId value.</param>
/// <param name="ActorId">The ActorId value.</param>
/// <param name="ExpectedRevision">The ExpectedRevision value.</param>
/// <param name="ExpiresUnixMilliseconds">The ExpiresUnixMilliseconds value.</param>
/// <param name="Arguments">The Arguments value.</param>
public sealed record DiagnosticCommand(Guid RequestId, string SubsystemId, string OperationId, string ActorId, long? ExpectedRevision, long ExpiresUnixMilliseconds, Dictionary<string, DiagnosticValue> Arguments);
