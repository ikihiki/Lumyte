namespace Lumyte.Interaction;

public sealed class ActionBinding<T> : ActionBinding
{
    public ActionBinding(InputAction<T> action, InputControl<T> control)
        : base(action, control)
    {
        TypedAction = action;
        TypedControl = control;
    }

    public InputAction<T> TypedAction { get; }

    public InputControl<T> TypedControl { get; }
}
