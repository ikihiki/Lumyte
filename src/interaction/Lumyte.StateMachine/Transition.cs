namespace Lumyte.StateMachine;

public sealed class Transition<TContext, TTrigger>
{
    private readonly List<Func<TContext, bool>> guards = [];
    private readonly List<Action<TContext>> effects = [];
    private bool frozen;

    public Transition(State<TContext> from, State<TContext> to, TTrigger trigger)
    {
        From = from ?? throw new ArgumentNullException(nameof(from));
        To = to ?? throw new ArgumentNullException(nameof(to));
        Trigger = trigger ?? throw new ArgumentNullException(nameof(trigger));
    }

    public State<TContext> From { get; }

    public State<TContext> To { get; }

    public TTrigger Trigger { get; }

    public int Priority { get; private set; }

    public Transition<TContext, TTrigger> When(Func<TContext, bool> guard)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(guard);
        guards.Add(guard);
        return this;
    }

    public Transition<TContext, TTrigger> Effect(Action<TContext> effect)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(effect);
        effects.Add(effect);
        return this;
    }

    public Transition<TContext, TTrigger> WithPriority(int priority)
    {
        EnsureMutable();
        Priority = priority;
        return this;
    }

    internal bool CanTake(TContext context)
    {
        foreach (Func<TContext, bool> guard in guards)
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
        foreach (Action<TContext> effect in effects)
        {
            effect(context);
        }
    }

    internal void Freeze() => frozen = true;

    private void EnsureMutable()
    {
        if (frozen)
        {
            throw new InvalidOperationException("A transition cannot be changed after it belongs to a state machine.");
        }
    }
}
