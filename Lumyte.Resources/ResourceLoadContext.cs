namespace Lumyte.Resources;

/// <summary>Provides the opened data and selector for one resource load.</summary>
public sealed class ResourceLoadContext
{
    internal ResourceLoadContext(
        AssetData data,
        string keyText,
        int selectorStart,
        ResourceStore store,
        ResourceLoadPath path)
    {
        Data = data;
        Selector = keyText.AsMemory(selectorStart);
        this.store = store;
        this.path = path;
    }

    private readonly ResourceStore store;
    private readonly ResourceLoadPath path;
    private readonly List<uint> dependencies = [];

    public AssetData Data { get; }

    public Stream Content => Data.Content;

    public ReadOnlyMemory<char> Selector { get; }

    public async ValueTask<ResourceHandle<T>> LoadAsync<T>(
        AssetKey<T> key,
        CancellationToken cancellationToken = default)
        where T : notnull
    {
        ResourceHandle<T> handle = await store
            .LoadDependencyAsync(key, path, cancellationToken)
            .ConfigureAwait(false);
        dependencies.Add(handle.Slot);
        return handle;
    }

    internal uint[] GetDependencies() => [.. dependencies];
}
