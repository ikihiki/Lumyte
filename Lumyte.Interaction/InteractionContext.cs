namespace Lumyte.Interaction;

public sealed class InteractionContext
{
    private readonly Dictionary<ContextKey, object?> values = [];

    public event EventHandler<ContextValueChangedEventArgs>? ValueChanged;

    public void Set<T>(ContextKey<T> key, T value)
    {
        ArgumentNullException.ThrowIfNull(key);
        bool hadValue = values.TryGetValue(key, out object? previous);
        if (hadValue && EqualityComparer<T>.Default.Equals((T?)previous!, value))
        {
            return;
        }

        values[key] = value;
        ValueChanged?.Invoke(this, new(key, hadValue ? previous : null, value));
    }

    public bool TryGet<T>(ContextKey<T> key, out T? value)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (values.TryGetValue(key, out object? stored))
        {
            value = (T?)stored;
            return true;
        }

        value = default;
        return false;
    }

    public T? GetValueOrDefault<T>(ContextKey<T> key) =>
        TryGet(key, out T? value) ? value : default;

    public bool Remove<T>(ContextKey<T> key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!values.Remove(key, out object? previous))
        {
            return false;
        }

        ValueChanged?.Invoke(this, new(key, previous, null));
        return true;
    }
}
