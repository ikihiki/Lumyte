namespace Lumyte.Interaction;

public sealed class ContextKey<T> : ContextKey
{
    internal ContextKey(string name)
        : base(name)
    {
    }

    public ContextCondition Is(T value) => ContextCondition.Equal(this, value);

    public ContextCondition IsNot(T value) => !Is(value);
}
