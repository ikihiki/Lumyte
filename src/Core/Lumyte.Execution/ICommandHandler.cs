namespace Lumyte.Execution;

/// <summary>A finite synchronous command handler.</summary>
/// <typeparam name="TCommand">The command payload type.</typeparam>
/// <typeparam name="TResult">The handler return type.</typeparam>
public interface ICommandHandler<TCommand, TResult>
    where TCommand : struct
{
    /// <summary>Executes one command on its declared target.</summary>
    /// <param name="command">The input.</param>
    /// <param name="context">The access verifier.</param>
    /// <returns>The actual result.</returns>
    TResult Execute(in TCommand command, in WorkContext context);
}
