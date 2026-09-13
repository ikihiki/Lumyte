namespace Lumyte.Graphics.Native.RenderGraph;

/// <summary>A reusable CPU blueprint. Instantiation supplies fresh execution references and immutable state.</summary>
public sealed class NativePassTemplate<TState>
{
    private readonly Action<NativePassBuildContext, TState> instantiate;
    public NativePassTemplate(Action<NativePassBuildContext, TState> instantiate)
    { ArgumentNullException.ThrowIfNull(instantiate); this.instantiate = instantiate; }
    internal void Build(NativePassBuildContext context, TState state) => instantiate(context, state);
}

/// <summary>Pass-owned immutable CPU preparation cached by a semantic key. GPU contents use generation tickets instead.</summary>
/// <remarks>The pass serializes calls. Failed or cancelled preparation is never installed in the cache.</remarks>
public sealed class NativePassPreparationCache<TKey, TValue> where TKey : notnull
{
    private readonly Dictionary<TKey, (TValue Value, LinkedListNode<TKey> Node)> values = [];
    private readonly LinkedList<TKey> recent = [];
    private readonly int capacity;
    public NativePassPreparationCache(int capacity = 64)
    { ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1); this.capacity = capacity; }
    public int Count => values.Count;
    public async ValueTask<TValue> GetOrCreateAsync(TKey key,
        Func<TKey, CancellationToken, ValueTask<TValue>> prepare, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepare); cancellationToken.ThrowIfCancellationRequested();
        if (values.TryGetValue(key, out var existing))
        { recent.Remove(existing.Node); recent.AddLast(existing.Node); return existing.Value; }
        TValue value = await prepare(key, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (values.Count == capacity) { Remove(recent.First!.Value); }
        values.Add(key, (value, recent.AddLast(key))); return value;
    }
    public bool Remove(TKey key)
    { if (!values.Remove(key, out var value)) { return false; } recent.Remove(value.Node); return true; }
    public void Clear() { values.Clear(); recent.Clear(); }
}
