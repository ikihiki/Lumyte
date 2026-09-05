namespace Lumyte.Resources;

internal interface IResourceRecord
{
    uint Slot { get; }

    uint Generation { get; }

    int DependencyCount { get; }

    ReadOnlyMemory<uint> Dependencies { get; }

    int ReferenceCount { get; }

    long LastAccessSequence { get; }

    int EvictionPriority { get; }

    ReadOnlyMemory<ResourceMemoryCost> MemoryCosts { get; }

    void Touch(long sequence);

    void AddReference();

    ValueTask ReleaseAsync();

    ValueTask<IResourceRecord> ReloadAsync(
        ResourceStore store,
        IReadOnlyDictionary<uint, IResourceRecord> candidates,
        CancellationToken cancellationToken);

    ValueTask DisposeAsync();
}

internal sealed class ResourceRecord<T>(
    AssetKey<T> key,
    uint slot,
    uint generation,
    T value,
    IResourceRecord[] dependencyRecords,
    ResourceMemoryCost[] memoryCosts,
    int evictionPriority,
    IResourceDispatcher dispatcher,
    ResourceExecutionLane disposalLane,
    Action<ReadOnlyMemory<ResourceMemoryCost>> memoryReleased) : IResourceRecord
    where T : notnull
{
    private int referenceCount = 1;
    private long lastAccessSequence;

    internal T Value { get; } = value;

    public uint Generation { get; } = generation;

    public uint Slot { get; } = slot;

    public int DependencyCount => dependencyRecords.Length;

    public ReadOnlyMemory<uint> Dependencies { get; } =
        dependencyRecords.Select(record => record.Slot).ToArray();

    public int ReferenceCount => Volatile.Read(ref referenceCount);

    public long LastAccessSequence => Volatile.Read(ref lastAccessSequence);

    public int EvictionPriority { get; } = evictionPriority;

    public ReadOnlyMemory<ResourceMemoryCost> MemoryCosts { get; } = memoryCosts;

    public void Touch(long sequence) => Volatile.Write(ref lastAccessSequence, sequence);

    public void AddReference()
    {
        int count = Interlocked.Increment(ref referenceCount);
        if (count <= 1)
        {
            Interlocked.Decrement(ref referenceCount);
            throw new ObjectDisposedException(nameof(ResourceRecord<T>));
        }
    }

    public async ValueTask ReleaseAsync()
    {
        int count = Interlocked.Decrement(ref referenceCount);
        if (count < 0)
        {
            throw new InvalidOperationException("The resource record was released too many times.");
        }

        if (count != 0)
        {
            return;
        }

        await dispatcher.InvokeAsync(disposalLane, DisposeAsync).ConfigureAwait(false);
        memoryReleased(MemoryCosts);
        for (int index = dependencyRecords.Length - 1; index >= 0; index--)
        {
            await dependencyRecords[index].ReleaseAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask<IResourceRecord> ReloadAsync(
        ResourceStore store,
        IReadOnlyDictionary<uint, IResourceRecord> candidates,
        CancellationToken cancellationToken) =>
        await store.LoadNextGenerationAsync(
            key,
            checked(Generation + 1),
            candidates,
            cancellationToken).ConfigureAwait(false);

    public async ValueTask DisposeAsync()
    {
        switch (Value)
        {
            case IAsyncDisposable asyncDisposable:
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                break;
            case IDisposable disposable:
                disposable.Dispose();
                break;
        }
    }
}
