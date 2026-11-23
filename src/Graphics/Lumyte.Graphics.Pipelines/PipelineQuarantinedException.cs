namespace Lumyte.Graphics.Pipelines;

/// <summary>Reports unknown submission acceptance without releasing dependent owners.</summary>
/// <param name="recoveryState">The retained generation and cleanup state.</param>
public sealed class PipelineQuarantinedException(PipelineRecoveryState recoveryState)
    : InvalidOperationException("Submission acceptance is unknown; retain the execution host and all dependent owners.")
{
    /// <summary>Gets the retained recovery state.</summary>
    public PipelineRecoveryState RecoveryState { get; } = recoveryState;
}
