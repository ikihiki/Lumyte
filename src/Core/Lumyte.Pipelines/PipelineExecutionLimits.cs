using Lumyte.Execution;

namespace Lumyte.Pipelines;

internal static class PipelineExecutionLimits
{
    internal static int MaxInFlight(ExecutionRuntime execution) => execution.PipelineMaxInFlightRuns;
}
