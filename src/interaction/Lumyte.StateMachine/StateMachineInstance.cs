using System.Diagnostics;

namespace Lumyte.StateMachine;

public sealed class StateMachineInstance<TContext, TTrigger>
{
    private readonly StateMachine<TContext, TTrigger> definition;

    internal StateMachineInstance(StateMachine<TContext, TTrigger> definition, TContext context)
    {
        this.definition = definition;
        Context = context ?? throw new ArgumentNullException(nameof(context));
        CurrentState = definition.InitialState;
        CurrentState.Enter(Context);
    }

    public TContext Context { get; }

    public State<TContext> CurrentState { get; private set; }

    public event Action<Transition<TContext, TTrigger>>? Transitioned;

    public bool Fire(TTrigger trigger)
    {
        using Activity? activity =
            StateMachineDiagnostics.Activities.StartActivity(
                "StateMachine.Fire",
                ActivityKind.Internal);
        activity?.SetTag("state_machine.state", CurrentState.Name);
        activity?.SetTag("state_machine.trigger", trigger?.ToString());
        try
        {
            Transition<TContext, TTrigger>? transition =
                definition.FindTransition(CurrentState, trigger, Context);
            if (transition is null)
            {
                activity?.SetTag("state_machine.transitioned", false);
                return false;
            }

            CurrentState.Exit(Context);
            transition.ApplyEffects(Context);
            CurrentState = transition.To;
            CurrentState.Enter(Context);
            activity?.SetTag("state_machine.transitioned", true);
            activity?.SetTag("state_machine.target", CurrentState.Name);
            activity?.SetTag("state_machine.priority", transition.Priority);
            Transitioned?.Invoke(transition);
            return true;
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity?.SetTag("error.type", exception.GetType().FullName);
            throw;
        }
    }

    public bool CanFire(TTrigger trigger) =>
        definition.FindTransition(CurrentState, trigger, Context) is not null;
}
