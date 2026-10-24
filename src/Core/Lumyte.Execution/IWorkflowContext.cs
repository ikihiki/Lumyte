namespace Lumyte.Execution;

/// <summary>Identifies the scheduler owning a captured workflow continuation.</summary>
internal interface IWorkflowContext
{
    /// <summary>Gets the runtime while the workflow remains alive.</summary>
    ExecutionRuntime? Runtime { get; }
}
