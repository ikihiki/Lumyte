namespace Lumyte.StateMachines;

/// <summary>A named state with ordered entry and exit actions, frozen when its definition is compiled.</summary>
/// <typeparam name="TContext">The application input and callback context type.</typeparam>
public sealed class State<TContext>
{
    private readonly List<Action<TContext>> _enterActions = [];
    private readonly List<Action<TContext>> _exitActions = [];
    private bool _frozen;

    /// <summary>Initializes a new instance of the <see cref="State{TContext}"/> class.</summary>
    /// <param name="name">The name.</param>
    public State(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    /// <summary>Gets the name.</summary>
    public string Name { get; }

    /// <summary>Registers an entry action, executed in registration order; compiled states cannot be changed.</summary>
    /// <param name="action">The action.</param>
    /// <returns>The computed result.</returns>
    public State<TContext> OnEnter(Action<TContext> action)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(action);
        _enterActions.Add(action);
        return this;
    }

    /// <summary>Registers an exit action, executed in registration order; compiled states cannot be changed.</summary>
    /// <param name="action">The action.</param>
    /// <returns>The computed result.</returns>
    public State<TContext> OnExit(Action<TContext> action)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(action);
        _exitActions.Add(action);
        return this;
    }

    internal void Freeze() => _frozen = true;

    internal void Enter(TContext context)
    {
        foreach (Action<TContext> action in _enterActions)
        {
            action(context);
        }
    }

    internal void Exit(TContext context)
    {
        foreach (Action<TContext> action in _exitActions)
        {
            action(context);
        }
    }

    private void EnsureMutable()
    {
        if (_frozen)
        {
            throw new InvalidOperationException("A compiled state cannot be changed.");
        }
    }
}
