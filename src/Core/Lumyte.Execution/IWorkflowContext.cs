namespace Lumyte.Execution;

/// <summary>Identifies the scheduler owning a captured workflow continuation.</summary>
internal interface IWorkflowContext
{
    /// <summary>Gets the runtime while the workflow remains alive.</summary>
    ExecutionRuntime? Runtime { get; }

    /// <summary>Checks elapsed time in the current synchronous dispatch.</summary>
    /// <param name="budget">The positive dispatch budget.</param>
    /// <returns>Whether the dispatch has consumed its budget.</returns>
    bool ShouldYield(TimeSpan budget);
}
