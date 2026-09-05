namespace Lumyte.Resources;

/// <summary>Retains one specific resource generation.</summary>
public sealed class ResourceLease<T> : IAsyncDisposable
    where T : notnull
{
    private IResourceRecord? record;

    internal ResourceLease(ResourceRecord<T> record)
    {
        record.AddReference();
        this.record = record;
        Value = record.Value;
        Generation = record.Generation;
    }

    public T Value { get; }

    public uint Generation { get; }

    public async ValueTask DisposeAsync()
    {
        IResourceRecord? owned = Interlocked.Exchange(ref record, null);
        if (owned is not null)
        {
            await owned.ReleaseAsync().ConfigureAwait(false);
        }
    }
}
