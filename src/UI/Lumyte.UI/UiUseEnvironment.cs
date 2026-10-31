namespace Lumyte.UI;

internal sealed class UiUseEnvironment(object key, object? value, UiUseEnvironment? parent)
{
    private object Key { get; } = key;

    private object? Value { get; } = value;

    private UiUseEnvironment? Parent { get; } = parent;

    internal static UiUseEnvironment Provide<T>(UiUseEnvironment? inner, UiContextKey<T> key, T value)
    {
        var entries = new List<UiUseEnvironment>();
        for (UiUseEnvironment? current = inner; current is not null; current = current.Parent)
        {
            entries.Add(current);
        }

        UiUseEnvironment result = new(key, value, null);
        for (int index = entries.Count - 1; index >= 0; index--)
        {
            UiUseEnvironment entry = entries[index];
            result = new(entry.Key, entry.Value, result);
        }

        return result;
    }

    internal static UiUseEnvironment? Overlay(UiUseEnvironment? inherited, UiUseEnvironment? local)
    {
        if (local is null)
        {
            return inherited;
        }

        var entries = new List<UiUseEnvironment>();
        for (UiUseEnvironment? current = local; current is not null; current = current.Parent)
        {
            entries.Add(current);
        }

        for (int index = entries.Count - 1; index >= 0; index--)
        {
            UiUseEnvironment entry = entries[index];
            inherited = new(entry.Key, entry.Value, inherited);
        }

        return inherited;
    }

    internal static T Read<T>(UiUseEnvironment? environment, UiContextKey<T> key)
    {
        for (UiUseEnvironment? current = environment; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current.Key, key))
            {
                return (T)current.Value!;
            }
        }

        throw new InvalidOperationException("No value was provided for the requested UI context.");
    }
}
