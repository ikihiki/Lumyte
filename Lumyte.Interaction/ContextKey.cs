namespace Lumyte.Interaction;

public abstract class ContextKey
{
    private protected ContextKey(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    public string Name { get; }

    public abstract Type ValueType { get; }

    public static ContextKey<T> Create<T>(string name) => new(name);

    public override string ToString() => Name;

    internal abstract ContextCondition EqualObject(object? value);
}
