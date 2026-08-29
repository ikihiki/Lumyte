namespace Lumyte.Interaction;

public abstract class ActionBinding
{
    private protected ActionBinding(InteractionIntent action, object control)
    {
        Action = action ?? throw new ArgumentNullException(nameof(action));
        Control = control ?? throw new ArgumentNullException(nameof(control));
    }

    public InteractionIntent Action { get; }

    public object Control { get; }
}
