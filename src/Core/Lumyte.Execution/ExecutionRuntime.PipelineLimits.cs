namespace Lumyte.Execution;

/// <summary>Exposes configured finite capacity to the private typed-pipeline lowering bridge.</summary>
public sealed partial class ExecutionRuntime
{
    internal int PipelineMaxInFlightRuns => _prepareAttempted ? _options.MaxInFlightRuns : throw new InvalidOperationException("Configure the borrowed execution runtime before graphics pipeline compilation.");
}
