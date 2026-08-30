namespace Lumyte.Resources;

public sealed record ResourceLoaderRegistration
{
    public ResourceLoaderRegistration(
        IResourceLoader loader,
        ResourceExecutionLane loadLane,
        ResourceExecutionLane disposalLane)
    {
        ArgumentNullException.ThrowIfNull(loader);
        if (string.IsNullOrWhiteSpace(loadLane.Name))
        {
            throw new ArgumentException("Resource execution lanes require a name.", nameof(loadLane));
        }

        if (string.IsNullOrWhiteSpace(disposalLane.Name))
        {
            throw new ArgumentException(
                "Resource execution lanes require a name.",
                nameof(disposalLane));
        }

        Loader = loader;
        LoadLane = loadLane;
        DisposalLane = disposalLane;
    }

    public ResourceLoaderRegistration(IResourceLoader loader)
        : this(loader, loader.LoadLane, loader.DisposalLane)
    {
    }

    public IResourceLoader Loader { get; }

    public ResourceExecutionLane LoadLane { get; }

    public ResourceExecutionLane DisposalLane { get; }
}
