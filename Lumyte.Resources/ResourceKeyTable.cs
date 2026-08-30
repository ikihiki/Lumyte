using System.Collections.Concurrent;

namespace Lumyte.Resources;

internal sealed class ResourceKeyTable
{
    private readonly ConcurrentDictionary<ResourceKeyIdentity, ResourceKeyEntry> entries = new();
    private int nextSlot = -1;

    internal int Count => entries.Count;

    internal ResourceKeyEntry GetOrAdd<T>(AssetKey<T> key)
    {
        string text = key.CanonicalText;
        ResourceKeyIdentity identity = new(text, typeof(T).TypeHandle);
        return entries.GetOrAdd(
            identity,
            static (value, state) => new ResourceKeyEntry(
                checked((uint)Interlocked.Increment(ref state.Table.nextSlot)),
                value.Text,
                value.ResultType,
                state.Key.AddressStart,
                state.Key.SelectorStart),
            (Table: this, Key: key));
    }

    private readonly record struct ResourceKeyIdentity(
        string Text,
        RuntimeTypeHandle ResultType);
}
