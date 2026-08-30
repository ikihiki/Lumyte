namespace Lumyte.Resources;

/// <summary>Coordinates resource resolution and owns the keys used by one resource store.</summary>
public sealed class ResourceStore
{
    private readonly IReadOnlyDictionary<string, IAssetResolver> resolvers;
    private readonly ResourceKeyTable keys = new();

    public ResourceStore(params IAssetResolver[] resolvers)
    {
        ArgumentNullException.ThrowIfNull(resolvers);

        Dictionary<string, IAssetResolver> registered = new(StringComparer.Ordinal);
        foreach (IAssetResolver resolver in resolvers)
        {
            ArgumentNullException.ThrowIfNull(resolver);
            string scheme = AssetKey.NormalizeScheme(resolver.Scheme);
            if (!registered.TryAdd(scheme, resolver))
            {
                throw new ArgumentException(
                    $"An asset resolver is already registered for the '{scheme}' scheme.",
                    nameof(resolvers));
            }
        }

        this.resolvers = registered;
    }

    internal int InternedKeyCount => keys.Count;

    public ValueTask<AssetLocation> ResolveAsync<T>(
        AssetKey<T> key,
        CancellationToken cancellationToken = default)
    {
        ResourceKeyEntry entry = keys.GetOrAdd(key);
        if (!resolvers.TryGetValue(entry.Scheme, out IAssetResolver? resolver))
        {
            throw new AssetResolutionException(
                $"No asset resolver is registered for the '{entry.Scheme}' scheme.");
        }

        return resolver.ResolveAsync(entry.Address, cancellationToken);
    }
}
