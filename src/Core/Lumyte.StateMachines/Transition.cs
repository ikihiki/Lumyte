namespace Lumyte.StateMachines;

/// <summary>A typed transition with pure guards, ordered effects and deterministic selection priority.</summary>
/// <typeparam name="TContext">The application input and callback context type.</typeparam>
/// <typeparam name="TTrigger">The trigger value type.</typeparam>
public sealed class Transition<TContext, TTrigger>
{
    private readonly List<Func<TContext, bool>> _guards = [];
    private readonly List<Action<TContext>> _effects = [];
    private bool _frozen;

    /// <summary>Initializes a new instance of the <see cref="Transition{TContext, TTrigger}"/> class.</summary>
    /// <param name="from">The from.</param>
    /// <param name="to">The to.</param>
    /// <param name="trigger">The trigger.</param>
    public Transition(State<TContext> from, State<TContext> to, TTrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        if (trigger is null)
        {
            throw new ArgumentNullException(nameof(trigger));
        }

        From = from;
        To = to;
        Trigger = trigger;
    }

    /// <summary>Gets the from.</summary>
    public State<TContext> From { get; }

    /// <summary>Gets the to.</summary>
    public State<TContext> To { get; }

    /// <summary>Gets the trigger.</summary>
    public TTrigger Trigger { get; }

    /// <summary>Gets the priority.</summary>
    public int Priority { get; private set; }

    /// <summary>Registers a pure guard combined with other guards using short-circuiting AND.</summary>
    /// <param name="guard">The guard.</param>
    /// <returns>The computed result.</returns>
    public Transition<TContext, TTrigger> When(Func<TContext, bool> guard)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(guard);
        _guards.Add(guard);
        return this;
    }

    /// <summary>Registers an ordered transition effect executed after exit and before destination entry.</summary>
    /// <param name="effect">The effect.</param>
    /// <returns>The computed result.</returns>
    public Transition<TContext, TTrigger> Effect(Action<TContext> effect)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(effect);
        _effects.Add(effect);
        return this;
    }

    /// <summary>Sets selection priority; higher values win and equal values retain registration order.</summary>
    /// <param name="priority">The priority.</param>
    /// <returns>The computed result.</returns>
    public Transition<TContext, TTrigger> WithPriority(int priority)
    {
        EnsureMutable();
        Priority = priority;
        return this;
    }

    internal void Freeze() => _frozen = true;

    internal bool CanTake(TContext context)
    {
        foreach (Func<TContext, bool> guard in _guards)
        {
            if (!guard(context))
            {
                return false;
            }
        }

        return true;
    }

    internal void ApplyEffects(TContext context)
    {
        foreach (Action<TContext> effect in _effects)
        {
            effect(context);
        }
    }

    private void EnsureMutable()
    {
        if (_frozen)
        {
            throw new InvalidOperationException("A compiled transition cannot be changed.");
        }
    }
}
