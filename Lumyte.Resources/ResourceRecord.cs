namespace Lumyte.Resources;

internal interface IResourceRecord
{
    uint Slot { get; }

    int DependencyCount { get; }

    ValueTask DisposeAsync();
}

internal sealed class ResourceRecord<T>(
    uint slot,
    uint generation,
    T value,
    uint[] dependencies) : IResourceRecord
    where T : notnull
{
    internal T Value { get; } = value;

    internal uint Generation { get; } = generation;

    public uint Slot { get; } = slot;

    public int DependencyCount => dependencies.Length;

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
