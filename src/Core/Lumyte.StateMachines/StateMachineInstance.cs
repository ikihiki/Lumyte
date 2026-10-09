using System.Diagnostics;

namespace Lumyte.StateMachines;

/// <summary>Executes typed synchronous state transitions with ordered callbacks and diagnostics.</summary>
/// <typeparam name="TContext">The application input and callback context type.</typeparam>
/// <typeparam name="TTrigger">The trigger value type.</typeparam>
public sealed class StateMachineInstance<TContext, TTrigger>
{
    private readonly List<TTrigger> _triggers = [];
    private StateMachine<TContext, TTrigger>.Candidate[] _candidates;
    private bool _evaluating;

    /// <summary>Initializes a new instance of the <see cref="StateMachineInstance{TContext, TTrigger}"/> class.</summary>
    /// <param name="definition">The definition.</param>
    /// <param name="context">The context.</param>
    public StateMachineInstance(StateMachine<TContext, TTrigger> definition, TContext context)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ValidateContext(context);
        Context = context;
        CurrentState = definition.InitialState;
        _candidates = definition.InitialCandidates;
        _evaluating = true;
        try
        {
            CurrentState.Enter(context);
        }
        finally
        {
            _evaluating = false;
        }
    }

    /// <summary>Notifies subscribers synchronously after a successful destination entry.</summary>
    public event Action<Transition<TContext, TTrigger>>? Transitioned;

    /// <summary>Gets the context.</summary>
    public TContext Context { get; }

    /// <summary>Gets the current state.</summary>
    public State<TContext> CurrentState { get; private set; }

    /// <summary>Selects and synchronously executes at most one eligible transition for the supplied trigger.</summary>
    /// <param name="trigger">The trigger.</param>
    /// <returns>The operation result.</returns>
    public bool Fire(TTrigger trigger) => Fire(trigger, Context);

    /// <summary>Evaluates guards without changing state or running transition callbacks.</summary>
    /// <param name="trigger">The trigger.</param>
    /// <returns>The operation result.</returns>
    public bool CanFire(TTrigger trigger) => CanFire(trigger, Context);

    /// <summary>Selects and synchronously executes at most one eligible transition for the supplied trigger.</summary>
    /// <param name="trigger">The trigger.</param>
    /// <param name="context">The context.</param>
    /// <returns>The operation result.</returns>
    public bool Fire(TTrigger trigger, TContext context)
    {
        ValidateContext(context);
        EnterEvaluation();
        Activity? activity = null;
        try
        {
            activity = StateMachineDiagnostics.Activities.StartActivity("StateMachine.Fire", ActivityKind.Internal);
            activity?.SetTag("state_machine.state", CurrentState.Name);
            activity?.SetTag("state_machine.trigger", trigger?.ToString());
            return Take(StateMachine<TContext, TTrigger>.Find(_candidates, trigger, context), context, activity);
        }
        catch (Exception exception)
        {
            RecordError(activity, exception);
            throw;
        }
        finally
        {
            FinishActivity(activity);
        }
    }

    /// <summary>Evaluates guards without changing state or running transition callbacks.</summary>
    /// <param name="trigger">The trigger.</param>
    /// <param name="context">The context.</param>
    /// <returns>The operation result.</returns>
    public bool CanFire(TTrigger trigger, TContext context)
    {
        ValidateContext(context);
        EnterEvaluation();
        try
        {
            return StateMachine<TContext, TTrigger>.Find(_candidates, trigger, context) >= 0;
        }
        finally
        {
            _evaluating = false;
        }
    }

    /// <summary>Selects at most one transition from a captured set of triggers and executes its callbacks.</summary>
    /// <param name="triggers">The triggers.</param>
    /// <param name="context">The context.</param>
    /// <returns>The operation result.</returns>
    public bool FireAny(IReadOnlyList<TTrigger> triggers, TContext context)
    {
        ArgumentNullException.ThrowIfNull(triggers);
        ValidateContext(context);
        EnterEvaluation();
        Activity? activity = null;
        try
        {
            activity = StateMachineDiagnostics.Activities.StartActivity("StateMachine.FireAny", ActivityKind.Internal);
            CaptureTriggers(triggers);
            activity?.SetTag("state_machine.state", CurrentState.Name);
            activity?.SetTag("state_machine.triggers", string.Join(",", _triggers));
            return Take(StateMachine<TContext, TTrigger>.FindAny(_candidates, _triggers, context), context, activity);
        }
        catch (Exception exception)
        {
            RecordError(activity, exception);
            throw;
        }
        finally
        {
            _triggers.Clear();
            FinishActivity(activity);
        }
    }

    /// <summary>Evaluates guards for a captured trigger set without changing state or running callbacks.</summary>
    /// <param name="triggers">The triggers.</param>
    /// <param name="context">The context.</param>
    /// <returns>The operation result.</returns>
    public bool CanFireAny(IReadOnlyList<TTrigger> triggers, TContext context)
    {
        ArgumentNullException.ThrowIfNull(triggers);
        ValidateContext(context);
        EnterEvaluation();
        try
        {
            CaptureTriggers(triggers);
            return StateMachine<TContext, TTrigger>.FindAny(_candidates, _triggers, context) >= 0;
        }
        finally
        {
            _triggers.Clear();
            _evaluating = false;
        }
    }

    private static void ValidateContext(TContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }
    }

    private static void RecordError(Activity? activity, Exception exception)
    {
        activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity?.SetTag("error.type", exception.GetType().FullName);
    }

    private void FinishActivity(Activity? activity)
    {
        try
        {
            activity?.Dispose();
        }
        finally
        {
            _evaluating = false;
        }
    }

    private void EnterEvaluation()
    {
        if (_evaluating)
        {
            throw new InvalidOperationException("A state machine cannot be evaluated recursively.");
        }

        _evaluating = true;
    }

    private void CaptureTriggers(IReadOnlyList<TTrigger> triggers)
    {
        _triggers.Clear();
        for (int index = 0; index < triggers.Count; index++)
        {
            TTrigger trigger = triggers[index];
            if (!_triggers.Contains(trigger))
            {
                _triggers.Add(trigger);
            }
        }
    }

    private bool Take(int candidateIndex, TContext context, Activity? activity)
    {
        activity?.SetTag("state_machine.transitioned", candidateIndex >= 0);
        if (candidateIndex < 0)
        {
            return false;
        }

        StateMachine<TContext, TTrigger>.Candidate candidate = _candidates[candidateIndex];
        Transition<TContext, TTrigger> transition = candidate.Transition;
        CurrentState.Exit(context);
        transition.ApplyEffects(context);
        CurrentState = transition.To;
        _candidates = candidate.TargetCandidates;
        CurrentState.Enter(context);
        activity?.SetTag("state_machine.target", CurrentState.Name);
        activity?.SetTag("state_machine.priority", transition.Priority);
        Transitioned?.Invoke(transition);
        return true;
    }
}
