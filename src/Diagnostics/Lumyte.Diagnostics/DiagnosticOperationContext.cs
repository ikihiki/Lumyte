namespace Lumyte.Diagnostics;

/// <summary>Trusted execution metadata supplied by the dispatcher.</summary>
/// <param name="RequestId">The RequestId argument.</param>
/// <param name="SessionId">The SessionId argument.</param>
/// <param name="Frame">The Frame argument.</param>
/// <param name="ActorId">The ActorId argument.</param>
/// <param name="ExpectedRevision">The ExpectedRevision argument.</param>
/// <param name="CancellationToken">The CancellationToken argument.</param>
public sealed record DiagnosticOperationContext(
    Guid RequestId, Guid SessionId, DiagnosticFrame Frame, string ActorId, long? ExpectedRevision, CancellationToken CancellationToken);
