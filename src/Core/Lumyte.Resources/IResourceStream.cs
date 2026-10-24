using Lumyte.Memory;

namespace Lumyte.Resources;

/// <summary>Creates independent readers from one resource factory generation.</summary>
/// <typeparam name="TElement">The unmanaged stream element, such as an audio frame.</typeparam>
/// <remarks>Factory ownership does not preserve external file versions; changed direct inputs may fault active readers.</remarks>
public interface IResourceStream<TElement>
    where TElement : unmanaged
{
    /// <summary>Creates a reader with its own cursor and decoder state.</summary>
    /// <param name="options">The session's bounded buffering configuration.</param>
    /// <param name="memory">Borrowed session memory; the reader must not dispose this scope.</param>
    /// <param name="cancellationToken">Cancels reader creation.</param>
    /// <returns>A reader owned by the session.</returns>
    ValueTask<IResourceStreamReader<TElement>> OpenReaderAsync(
        ResourceStreamOptions options,
        IMemoryScope memory,
        CancellationToken cancellationToken = default);
}
