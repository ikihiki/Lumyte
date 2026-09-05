namespace Lumyte.StateMachine;

public static partial class StateMachineKit
{
    public static Transition<TContext, TTrigger> Transition<TContext, TTrigger>(
        State<TContext> from,
        State<TContext> to,
        TTrigger trigger) =>
        new(from, to, trigger);
}
