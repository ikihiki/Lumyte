namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Frozen command routes and their actual legacy physical execution contract.</summary>
/// <param name="Kind">The direct-dispatch key.</param>
/// <param name="CommandType">The immutable typed input.</param>
/// <param name="BackendType">The backend contract resolved during construction.</param>
/// <param name="SharedIdentity">The explicit graph-local shared identity, or null for a borrowed leaf.</param>
/// <param name="ConcreteBackendType">The actual borrowed backend or shared manager type.</param>
/// <param name="ChoiceBackends">The explicit FirstSupported candidate order, without running predicates.</param>
/// <param name="Execution">The actual CPU/transfer/GPU contract.</param>
public sealed record CommandRouteDescription2D(DrawKind2D Kind, Type CommandType, Type BackendType, string? SharedIdentity, Type ConcreteBackendType, IReadOnlyList<Type> ChoiceBackends, string Execution);
