using Lumyte.Composition;

namespace Lumyte.StateMachine;

[Composable(Factory = "StateMachineKit", Name = "State")]
public sealed partial class State<TContext>
{
    private readonly List<Action<TContext>> enterActions = [];
    private readonly List<Action<TContext>> exitActions = [];
    private string name = string.Empty;
    private bool frozen;

    [ComposeParameter]
    public required string Name
    {
        get => name;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            name = value;
        }
    }

    public State<TContext> OnEnter(Action<TContext> action)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(action);
        enterActions.Add(action);
        return this;
    }

    public State<TContext> OnExit(Action<TContext> action)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(action);
        exitActions.Add(action);
        return this;
    }

    internal void Enter(TContext context)
    {
        foreach (Action<TContext> action in enterActions)
        {
            action(context);
        }
    }

    internal void Exit(TContext context)
    {
        foreach (Action<TContext> action in exitActions)
        {
            action(context);
        }
    }

    internal void Freeze() => frozen = true;

    private void EnsureMutable()
    {
        if (frozen)
        {
            throw new InvalidOperationException("A state cannot be changed after it belongs to a state machine.");
        }
    }
}
