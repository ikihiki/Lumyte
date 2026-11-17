namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Frozen command routes and their actual legacy physical execution contract.</summary>
/// <param name="Kind">The direct-dispatch key.</param>
/// <param name="CommandType">The immutable typed input.</param>
/// <param name="BackendType">The backend contract resolved during construction.</param>
/// <param name="SharedIdentity">The optional first candidate diagnostic name, never a sharing key.</param>
/// <param name="ConcreteBackendType">The actual borrowed backend or shared manager type.</param>
/// <param name="ChoiceBackends">The explicit slot candidate order, without running predicates.</param>
/// <param name="Execution">The actual CPU/transfer/GPU contract.</param>
public sealed record CommandRouteDescription2D(DrawKind2D Kind, Type CommandType, Type BackendType, string? SharedIdentity, Type ConcreteBackendType, IReadOnlyList<Type> ChoiceBackends, string Execution)
{
    /// <summary>Gets structural candidate paths; diagnostic names are never sharing identities.</summary>
    public IReadOnlyList<string> CandidatePaths { get; init; } = Array.Empty<string>();

    /// <summary>Gets all frozen candidate and nested child placement paths in structural order.</summary>
    public IReadOnlyList<string> OccurrencePaths { get; init; } = Array.Empty<string>();

    /// <summary>Gets every ordered declaration identity from the compiled slot snapshot.</summary>
    public IReadOnlyList<string?> CandidateIdentities { get; init; } = Array.Empty<string?>();
}
