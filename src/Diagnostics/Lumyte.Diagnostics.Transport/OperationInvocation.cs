namespace Lumyte.Diagnostics.Transport;

/// <summary>An operator request; actor and permissions are assigned by the server.</summary>
/// <param name="RequestId">The RequestId value.</param>
/// <param name="SubsystemId">The SubsystemId value.</param>
/// <param name="OperationId">The OperationId value.</param>
/// <param name="Arguments">The Arguments value.</param>
/// <param name="ExpectedRevision">The ExpectedRevision value.</param>
/// <param name="TimeoutMilliseconds">The TimeoutMilliseconds value.</param>
public sealed record OperationInvocation(Guid RequestId, string SubsystemId, string OperationId, Dictionary<string, DiagnosticValue> Arguments, long? ExpectedRevision = null, int TimeoutMilliseconds = 5000);
