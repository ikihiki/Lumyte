namespace Lumyte.Interaction;

public sealed class ContextKeyRegistry
{
    private readonly Dictionary<string, ContextKey> keys = new(StringComparer.Ordinal);

    public void Register(ContextKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!keys.TryAdd(key.Name, key))
        {
            throw new ArgumentException($"The context key '{key.Name}' is already registered.", nameof(key));
        }
    }

    public bool TryGet(string name, out ContextKey? key) => keys.TryGetValue(name, out key);
}
