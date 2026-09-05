namespace Lumyte.Resources;

public sealed record ResourceResolverRegistration
{
    public ResourceResolverRegistration(
        IAssetResolver resolver,
        ResourceExecutionLane openLane)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        if (string.IsNullOrWhiteSpace(openLane.Name))
        {
            throw new ArgumentException("Resource execution lanes require a name.", nameof(openLane));
        }

        Resolver = resolver;
        OpenLane = openLane;
    }

    public ResourceResolverRegistration(IAssetResolver resolver)
        : this(resolver, ResourceExecutionLane.Default)
    {
    }

    public IAssetResolver Resolver { get; }

    public ResourceExecutionLane OpenLane { get; }
}
