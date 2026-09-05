namespace Lumyte.Resources;

public sealed record ResourceStoreConfiguration
{
    public IEnumerable<ResourceResolverRegistration> Resolvers { get; init; } =
        Array.Empty<ResourceResolverRegistration>();

    public IEnumerable<ResourceLoaderRegistration> Loaders { get; init; } =
        Array.Empty<ResourceLoaderRegistration>();

    public ResourceStoreOptions Options { get; init; } = new();

    public IResourceDispatcher Dispatcher { get; init; } = new InlineResourceDispatcher();
}
