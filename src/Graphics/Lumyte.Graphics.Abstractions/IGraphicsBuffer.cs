namespace Lumyte.Graphics.Abstractions;

/// <summary>
/// Owns a typed GPU allocation implemented directly by the backend.
/// </summary>
/// <typeparam name="T">The unmanaged element type; shader ABI compatibility is validated separately.</typeparam>
public interface IGraphicsBuffer<T> : IDisposable
    where T : unmanaged
{
    /// <summary>
    /// Gets the native allocation size in bytes.
    /// </summary>
    ulong SizeInBytes { get; }

    /// <summary>
    /// Gets the backend-resolved element layout and copy constraints.
    /// </summary>
    BufferLayout<T> Layout { get; }

    /// <summary>
    /// Gets the allocation count in elements.
    /// </summary>
    ulong Count { get; }

    /// <summary>
    /// Gets the immutable permitted buffer usages.
    /// </summary>
    BufferUsage Usage { get; }

    /// <summary>
    /// Gets the CPU access preference chosen at allocation.
    /// </summary>
    MemoryPreference Memory { get; }

    /// <summary>Gets a value indicating whether CPU mapping has completed and remains active.</summary>
    bool IsMapped { get; }

    /// <summary>
    /// Rejects empty, overflowing, or out-of-bounds byte ranges.
    /// </summary>
    /// <param name="offset">The start offset in bytes.</param>
    /// <param name="length">The byte length of the backend range.</param>
    void ValidateRange(ulong offset, ulong length);

    /// <summary>
    /// Copies caller bytes into idle Upload storage without GPU transfer.
    /// </summary>
    /// <param name="source">The caller-owned source bytes.</param>
    /// <param name="offset">The start offset in bytes.</param>
    /// <param name="length">The byte length of the backend range.</param>
    void CopyFrom(ReadOnlySpan<byte> source, ulong offset, ulong length);

    /// <summary>
    /// Copies completed Readback bytes into caller storage without waiting.
    /// </summary>
    /// <param name="destination">The caller-owned destination bytes.</param>
    /// <param name="offset">The start offset in bytes.</param>
    /// <param name="length">The byte length of the backend range.</param>
    void CopyTo(Span<byte> destination, ulong offset, ulong length);

    /// <summary>Explicitly maps the whole Upload or Readback allocation for CPU access.</summary>
    /// <param name="cancellationToken">Cancels retaining a mapping after the native request completes.</param>
    /// <returns>A task that completes when CPU access is available.</returns>
    ValueTask MapAsync(CancellationToken cancellationToken = default);

    /// <summary>Releases CPU mapping without recording or submitting GPU commands.</summary>
    void Unmap();

    /// <summary>
    /// Creates a non-owning, nonempty element range within the allocation.
    /// </summary>
    /// <param name="offset">The start offset in elements.</param>
    /// <param name="count">The number of elements; no implicit padding or rounding is applied.</param>
    /// <returns>The non-owning element range; the allocation lifetime is unchanged.</returns>
    BufferSlice<T> Slice(ulong offset, ulong count);

    /// <summary>
    /// Copies into explicitly mapped Upload memory without GPU commands, staging allocation, or submission.
    /// </summary>
    /// <param name="source">The elements to copy; their count must not exceed the Upload range.</param>
    void CopyFrom(ReadOnlySpan<T> source);

    /// <summary>
    /// Copies explicitly mapped Readback memory into caller storage without GPU work or completion waits.
    /// </summary>
    /// <param name="destination">The caller storage, which must fit the complete Readback range.</param>
    void CopyTo(Span<T> destination);
}
