using System.Runtime.CompilerServices;

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

    ValueTask<T> LoadAsync(
        ResourceLoadContext context,
        CancellationToken cancellationToken = default);

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
