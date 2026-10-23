namespace Lumyte.Execution;

internal abstract class GraphState(ExecutionRuntime runtime, JobRun run, CancellationToken token)
{
    internal ExecutionRuntime Runtime { get; } = runtime;

    internal JobRun Run { get; } = run;

    internal CancellationToken Token { get; } = token;

    internal bool IsCancellationRequested => Run.IsCancellationRequested || Runtime.CancelPending || Token.IsCancellationRequested;

    internal abstract bool HasPropagation { get; }

    internal abstract void Step();

    internal abstract void Clear();
}
