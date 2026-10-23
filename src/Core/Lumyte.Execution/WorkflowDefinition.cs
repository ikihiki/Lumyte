using System.Runtime.CompilerServices;

namespace Lumyte.Execution;

internal abstract class WorkflowDefinition<TCommand, TResult>(ModuleDefinition module, string name)
    : WorkDefinition(module, name, new WorkOptions { Target = WorkTarget.Cpu }, [])
    where TCommand : struct
{
    internal override long PayloadBytes => Unsafe.SizeOf<TCommand>() + Unsafe.SizeOf<TResult>() + 512L;

    internal abstract ValueTask<TResult> Invoke(TCommand command, CancellationToken token);
}
