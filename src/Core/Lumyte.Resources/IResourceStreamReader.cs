namespace Lumyte.Resources;

/// <summary>Reads one immutable resource revision with an independent cursor.</summary>
/// <typeparam name="TElement">The unmanaged stream element.</typeparam>
public interface IResourceStreamReader<TElement> : IAsyncDisposable
    where TElement : unmanaged
{
    /// <summary>Gets a value indicating whether the reader supports seeking without fully buffering its input.</summary>
    bool CanSeek { get; }

    /// <summary>Reads sequential elements. A short read is permitted; zero denotes the end of the stream.</summary>
    /// <param name="destination">Storage for decoded elements.</param>
    /// <param name="cancellationToken">Cancels the reader operation.</param>
    /// <returns>The number of elements written.</returns>
    ValueTask<int> ReadAsync(Memory<TElement> destination, CancellationToken cancellationToken = default);

    /// <summary>Moves the reader to an element offset.</summary>
    /// <param name="elementOffset">The absolute element offset, not a compressed byte offset.</param>
    /// <param name="cancellationToken">Cancels seeking.</param>
    /// <returns>Completion of the seek.</returns>
    ValueTask SeekAsync(ulong elementOffset, CancellationToken cancellationToken = default);
}
