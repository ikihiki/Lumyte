namespace Lumyte.Resources;

/// <summary>Provides the opened data and selector for one resource load.</summary>
public sealed class ResourceLoadContext
{
    internal ResourceLoadContext(
        AssetData data,
        string keyText,
        int selectorStart,
        ResourceStore store,
        ResourceLoadPath path,
        IReadOnlyDictionary<uint, IResourceRecord>? candidates)
    {
        Data = data;
        Selector = new ResourceSelector(keyText.AsMemory(selectorStart));
        this.store = store;
        this.path = path;
        this.candidates = candidates;
    }

    private readonly ResourceStore store;
    private readonly ResourceLoadPath path;
    private readonly IReadOnlyDictionary<uint, IResourceRecord>? candidates;
    private readonly List<IResourceRecord> dependencies = [];

    public AssetData Data { get; }

    public Stream Content => Data.Content;

    public ResourceSelector Selector { get; }

    public async ValueTask<ResourceDependency<T>> LoadAsync<T>(
        AssetKey<T> key,
        CancellationToken cancellationToken = default)
        where T : notnull
    {
        ResourceRecord<T> record = await store
            .LoadDependencyRecordAsync(key, path, candidates, cancellationToken)
            .ConfigureAwait(false);
        record.AddReference();
        dependencies.Add(record);
        return new ResourceDependency<T>(
            new ResourceId<T>(record.Slot),
            record.Value,
            record.Generation);
    }

    internal IResourceRecord[] TakeDependencies()
    {
        IResourceRecord[] result = [.. dependencies];
        dependencies.Clear();
        return result;
    }

    internal async ValueTask ReleaseDependenciesAsync()
    {
        for (int index = dependencies.Count - 1; index >= 0; index--)
        {
            await dependencies[index].ReleaseAsync().ConfigureAwait(false);
        }

        dependencies.Clear();
    }
}
