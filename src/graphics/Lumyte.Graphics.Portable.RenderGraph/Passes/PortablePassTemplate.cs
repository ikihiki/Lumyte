namespace Lumyte.Graphics.Portable.RenderGraph;

/// <summary>An immutable CPU expansion recipe reused with an independently fixed state on each execution.</summary>
public sealed class PortablePassTemplate<TState>
{
    private readonly Action<PortablePassBuildContext, TState> expand;
    public PortablePassTemplate(Action<PortablePassBuildContext, TState> expand)
    { this.expand = expand ?? throw new ArgumentNullException(nameof(expand)); }
    internal void Expand(PortablePassBuildContext context, TState state) => expand(context, state);
}

/// <summary>A pass-owned cache of immutable CPU preparation. Keys include every dependency; GPU ownership uses content tickets.</summary>
public sealed class PortablePassPreparationCache<TKey, TValue> : IDisposable where TKey : notnull
{
    private readonly Dictionary<TKey, TValue> values;
    private readonly LinkedList<TKey> order = [];
    private readonly int maxEntries;
    private bool disposed;
    public PortablePassPreparationCache(int maxEntries = 64, IEqualityComparer<TKey>? comparer = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEntries);
        this.maxEntries = maxEntries; values = new(comparer);
    }
    public int Count => values.Count;
    /// <remarks>Call from serialized pass builds. Failed or cancelled preparation is never cached.</remarks>
    public async ValueTask<TValue> GetOrCreateAsync(TKey key, Func<TKey, CancellationToken, ValueTask<TValue>> create,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this); ArgumentNullException.ThrowIfNull(create);
        cancellationToken.ThrowIfCancellationRequested();
        if (values.TryGetValue(key, out TValue? value)) { Touch(key); return value; }
        value = await create(key, cancellationToken).ConfigureAwait(false);
        ObjectDisposedException.ThrowIf(disposed, this); cancellationToken.ThrowIfCancellationRequested();
        if (values.Count == maxEntries) { Remove(order.First!.Value); }
        values.Add(key, value); order.AddLast(key); return value;
    }
    private void Touch(TKey key)
    { LinkedListNode<TKey>? node = order.First; while (node is not null && !values.Comparer.Equals(node.Value, key)) { node = node.Next; } if (node is not null) { order.Remove(node); order.AddLast(node); } }
    public bool Remove(TKey key)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!values.Remove(key)) { return false; }
        LinkedListNode<TKey>? node = order.First; while (node is not null && !values.Comparer.Equals(node.Value, key)) { node = node.Next; }
        if (node is not null) { order.Remove(node); } return true;
    }
    public void Clear() { ObjectDisposedException.ThrowIf(disposed, this); values.Clear(); order.Clear(); }
    public void Dispose() { if (disposed) { return; } Clear(); disposed = true; }
}
