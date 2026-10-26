namespace Lumyte.Execution;

/// <summary>An opaque, typed command definition.</summary>
/// <typeparam name="TCommand">The command payload type.</typeparam>
/// <typeparam name="TResult">The handler return type.</typeparam>
public readonly struct WorkKey<TCommand, TResult>
    where TCommand : struct
{
    internal WorkKey(WorkDefinition<TCommand, TResult> definition)
    {
        Definition = definition;
    }

    internal WorkDefinition<TCommand, TResult>? Definition { get; }
}
