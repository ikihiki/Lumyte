namespace Lumyte.Resources;

internal interface IResourceRecord
{
    uint Slot { get; }

    uint Generation { get; }

    int DependencyCount { get; }

    ReadOnlyMemory<uint> Dependencies { get; }

    ValueTask<IResourceRecord> ReloadAsync(
        ResourceStore store,
        CancellationToken cancellationToken);

    ValueTask DisposeAsync();
}

internal sealed class ResourceRecord<T>(
    AssetKey<T> key,
    uint slot,
    uint generation,
    T value,
    uint[] dependencies) : IResourceRecord
    where T : notnull
{
    internal T Value { get; } = value;

    public uint Generation { get; } = generation;

    public uint Slot { get; } = slot;

    public int DependencyCount => dependencies.Length;

    public ReadOnlyMemory<uint> Dependencies => dependencies;

    public async ValueTask<IResourceRecord> ReloadAsync(
        ResourceStore store,
        CancellationToken cancellationToken) =>
        await store.LoadNextGenerationAsync(
            key,
            checked(Generation + 1),
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
