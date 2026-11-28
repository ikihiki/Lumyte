namespace Lumyte.Pipelines;

/// <summary>Explicit borrowed backend. Core owns validation order, factories, processors and run cleanup.</summary>
public interface IPipelineBackend
{
    /// <summary>Validates each compiled reservation before any processor is created.</summary>
    /// <param name="node">The zero-based node.</param>
    /// <param name="requirements">The captured finite requirements.</param>
    void Validate(int node, StageRequirements requirements);

    /// <summary>Supplies immutable build capabilities for one node.</summary>
    /// <param name="node">The zero-based node.</param>
    /// <returns>The borrowed capabilities.</returns>
    IStageCapabilities BuildCapabilities(int node);

    /// <summary>Validates actual extensions against the compiled reservation and creates independent invocation capabilities.</summary>
    /// <remarks>Reject unknown or growing requirements. Never reuse mutable scope bindings across nodes or runs; do not allocate native resources until Bind can register rollback ownership.</remarks>
    /// <param name="node">The zero-based node.</param>
    /// <param name="compiled">The compiled reservation.</param>
    /// <param name="actual">The actual requirements to validate before invocation.</param>
    /// <returns>The invocation capabilities, including an optional transaction binding.</returns>
    IStageCapabilities PrepareCapabilities(int node, StageRequirements compiled, StageRequirements actual);
}
