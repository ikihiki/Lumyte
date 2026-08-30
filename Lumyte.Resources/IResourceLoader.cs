using System.Runtime.CompilerServices;

namespace Lumyte.Resources;

/// <summary>Loads typed resources from opened asset data.</summary>
public interface IResourceLoader
{
    Type ResourceType { get; }

    ResourceExecutionLane LoadLane { get; }

    ResourceExecutionLane DisposalLane { get; }

    int EvictionPriority { get; }

    ValueTask<T> LoadAsync<T>(
        ResourceLoadContext context,
        CancellationToken cancellationToken = default)
        where T : notnull;

    IReadOnlyList<ResourceMemoryCost> Measure<T>(T resource)
        where T : notnull;
}

/// <summary>Marks a resource type supported by a loader.</summary>
public interface IResourceLoader<T> : IResourceLoader
    where T : notnull
{
    new static Type ResourceType => typeof(T);

    Type IResourceLoader.ResourceType => ResourceType;

    new ResourceExecutionLane LoadLane => ResourceExecutionLane.Default;

    ResourceExecutionLane IResourceLoader.LoadLane => LoadLane;

    new ResourceExecutionLane DisposalLane => LoadLane;

    ResourceExecutionLane IResourceLoader.DisposalLane => DisposalLane;

    new int EvictionPriority => 0;

    int IResourceLoader.EvictionPriority => EvictionPriority;

    ValueTask<T> LoadAsync(
        ResourceLoadContext context,
        CancellationToken cancellationToken = default);

    IReadOnlyList<ResourceMemoryCost> Measure(T resource) =>
        Array.Empty<ResourceMemoryCost>();

    IReadOnlyList<ResourceMemoryCost> IResourceLoader.Measure<TResult>(TResult resource)
    {
        if (typeof(TResult) != typeof(T))
        {
            throw new InvalidOperationException(
                $"The loader for '{typeof(T)}' cannot measure '{typeof(TResult)}'.");
        }

        TResult value = resource;
        return Measure(Unsafe.As<TResult, T>(ref value));
    }

    async ValueTask<TResult> IResourceLoader.LoadAsync<TResult>(
        ResourceLoadContext context,
        CancellationToken cancellationToken)
    {
        if (typeof(TResult) != typeof(T))
        {
            throw new InvalidOperationException(
                $"The loader for '{typeof(T)}' cannot load '{typeof(TResult)}'.");
        }

        T resource = await LoadAsync(context, cancellationToken).ConfigureAwait(false);
        return Unsafe.As<T, TResult>(ref resource);
    }
}
