namespace Lumyte.Execution;

internal abstract class WorkDefinition<TCommand, TResult>(ModuleDefinition module, string name, WorkOptions options, GateDefinition[] gates)
    : WorkDefinition(module, name, options, gates)
    where TCommand : struct
{
    internal override long PayloadBytes => System.Runtime.CompilerServices.Unsafe.SizeOf<TCommand>() + System.Runtime.CompilerServices.Unsafe.SizeOf<TResult>() + 256L;

    internal abstract TResult Invoke(in TCommand command, in WorkContext context);
}
