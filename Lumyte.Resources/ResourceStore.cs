namespace Lumyte.Resources;

/// <summary>Coordinates resource resolution and owns the keys used by one resource store.</summary>
public sealed class ResourceStore
{
    private readonly IReadOnlyDictionary<string, IAssetResolver> resolvers;
    private readonly IReadOnlyDictionary<Type, IResourceLoader> loaders;
    private readonly ResourceKeyTable keys = new();

    public ResourceStore(
        IEnumerable<IAssetResolver> resolvers,
        IEnumerable<IResourceLoader> loaders)
    {
        ArgumentNullException.ThrowIfNull(resolvers);
        ArgumentNullException.ThrowIfNull(loaders);

        Dictionary<string, IAssetResolver> registeredResolvers = new(StringComparer.Ordinal);
        foreach (IAssetResolver resolver in resolvers)
        {
            ArgumentNullException.ThrowIfNull(resolver);
            string scheme = AssetKey.NormalizeScheme(resolver.Scheme);
            if (!registeredResolvers.TryAdd(scheme, resolver))
            {
                throw new ArgumentException(
                    $"An asset resolver is already registered for the '{scheme}' scheme.",
                    nameof(resolvers));
            }
        }

        Dictionary<Type, IResourceLoader> registeredLoaders = [];
        foreach (IResourceLoader loader in loaders)
        {
            ArgumentNullException.ThrowIfNull(loader);
            Type resourceType = loader.ResourceType;
            if (!registeredLoaders.TryAdd(resourceType, loader))
            {
                throw new ArgumentException(
                    $"A resource loader is already registered for '{resourceType}'.",
                    nameof(loaders));
            }
        }

        this.resolvers = registeredResolvers;
        this.loaders = registeredLoaders;
    }

    internal int InternedKeyCount => keys.Count;

    public async ValueTask<T> LoadAsync<T>(
        AssetKey<T> key,
        CancellationToken cancellationToken = default)
        where T : notnull
    {
        ResourceKeyEntry entry = keys.GetOrAdd(key);
        if (!resolvers.TryGetValue(entry.Scheme, out IAssetResolver? resolver))
        {
            throw new AssetResolutionException(
                $"No asset resolver is registered for the '{entry.Scheme}' scheme.");
        }

        if (!loaders.TryGetValue(typeof(T), out IResourceLoader? loader))
        {
            throw new ResourceLoaderNotFoundException(
                $"No resource loader is registered for '{typeof(T)}'.");
        }

        await using AssetData data = await resolver
            .OpenAsync(entry.Address, cancellationToken)
            .ConfigureAwait(false);
        ResourceLoadContext context = new(data, entry.Text, entry.SelectorStart);

        try
        {
            return await loader
                .LoadAsync<T>(context, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ResourceException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ResourceLoadException(
                $"The resource '{key}' could not be loaded.",
                exception);
        }
    }

}
