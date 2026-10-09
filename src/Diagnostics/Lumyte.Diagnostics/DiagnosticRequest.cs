namespace Lumyte.Diagnostics;

/// <summary>An authenticated host request; permissions never come from untrusted payloads.</summary>
/// <param name="SubsystemId">The SubsystemId argument.</param>
/// <param name="OperationId">The OperationId argument.</param>
/// <param name="Context">The Context argument.</param>
/// <param name="Arguments">The Arguments argument.</param>
/// <param name="Permissions">The Permissions argument.</param>
/// <param name="DeadlineTimestamp">The DeadlineTimestamp argument.</param>
public sealed record DiagnosticRequest(string SubsystemId, string OperationId, DiagnosticOperationContext Context, IReadOnlyDictionary<string, DiagnosticValue> Arguments, IReadOnlySet<DiagnosticPermission> Permissions, long DeadlineTimestamp);
