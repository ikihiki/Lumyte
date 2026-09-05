namespace Lumyte.Resources;

/// <summary>Provides a stable view of all loaded resource generations at one point in time.</summary>
public sealed class ResourceSnapshot : IAsyncDisposable
{
    private readonly ResourceStore store;
    private readonly IReadOnlyDictionary<uint, IResourceRecord> records;
    private int disposed;

    internal ResourceSnapshot(
        ResourceStore store,
        IReadOnlyDictionary<uint, IResourceRecord> records)
    {
        this.store = store;
        this.records = records;
        foreach (IResourceRecord record in records.Values)
        {
            record.AddReference();
        }
    }

    public T Get<T>(ResourceId<T> id)
        where T : notnull =>
        GetRecord(id).Value;

    public T Get<T>(ResourceHandle<T> handle)
        where T : notnull =>
        GetRecord(handle).Value;

    public uint GetGeneration<T>(ResourceId<T> id)
        where T : notnull =>
        GetRecord(id).Generation;

    public uint GetGeneration<T>(ResourceHandle<T> handle)
        where T : notnull =>
        GetRecord(handle).Generation;

    public ResourceLease<T> Lease<T>(ResourceId<T> id)
        where T : notnull
    {
        ResourceRecord<T> record = GetRecord(id);
        return new ResourceLease<T>(record);
    }

    public ResourceLease<T> Lease<T>(ResourceHandle<T> handle)
        where T : notnull
    {
        ResourceRecord<T> record = GetRecord(handle);
        return new ResourceLease<T>(record);
    }

    private ResourceRecord<T> GetRecord<T>(ResourceHandle<T> handle)
        where T : notnull
    {
        if (!ReferenceEquals(store, handle.Store))
        {
            throw new ArgumentException(
                "The resource handle belongs to a different resource store.",
                nameof(handle));
        }

        return GetRecord(handle.Id);
    }

    private ResourceRecord<T> GetRecord<T>(ResourceId<T> id)
        where T : notnull
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        if (!records.TryGetValue(id.Slot, out IResourceRecord? record))
        {
            throw new ResourceNotFoundException(
                $"The resource slot '{id.Slot}' is not present in this snapshot.");
        }

        return (ResourceRecord<T>)record;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        foreach (IResourceRecord record in records.Values.Reverse())
        {
            await record.ReleaseAsync().ConfigureAwait(false);
        }
    }
}
