namespace Lumyte.Resources;

/// <summary>Loads typed resources from opened asset data.</summary>
public interface IResourceLoader
{
    Type ResourceType { get; }

    ValueTask<T> LoadAsync<T>(
        ResourceLoadContext context,
        CancellationToken cancellationToken = default)
        where T : notnull;
}

/// <summary>Marks a resource type supported by a loader.</summary>
public interface IResourceLoader<T> : IResourceLoader
    where T : notnull
{
    new static Type ResourceType => typeof(T);

    Type IResourceLoader.ResourceType => ResourceType;
}
