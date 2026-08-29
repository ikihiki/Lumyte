namespace Lumyte.Interaction;

public sealed class ActionBinding<T> : ActionBinding
{
    private readonly InputProcessor<T>[] processors;

    public ActionBinding(
        InputAction<T> action,
        InputControl<T> control,
        params ReadOnlySpan<InputProcessor<T>> processors)
        : base(action, control)
    {
        TypedAction = action;
        TypedControl = control;
        this.processors = processors.ToArray();
    }

    public InputAction<T> TypedAction { get; }

    public InputControl<T> TypedControl { get; }

    public IReadOnlyList<InputProcessor<T>> Processors => processors;

    public T Process(T value)
    {
        foreach (InputProcessor<T> processor in processors)
        {
            value = processor(value);
        }

        return value;
    }
}
