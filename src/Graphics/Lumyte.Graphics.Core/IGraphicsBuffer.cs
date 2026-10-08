namespace Lumyte.Graphics;

/// <summary>
/// Owns a typed GPU allocation implemented directly by the backend.
/// </summary>
/// <typeparam name="T">The unmanaged element type; shader ABI compatibility is validated separately.</typeparam>
public interface IGraphicsBuffer<T> : IDisposable
    where T : unmanaged
{
    /// <summary>
    /// Gets the backend-resolved element layout and copy constraints.
    /// </summary>
    BufferLayout<T> Layout { get; }

    /// <summary>
    /// Gets the allocation count in elements.
    /// </summary>
    ulong Count { get; }

    /// <summary>
    /// Gets the checked allocation size in bytes.
    /// </summary>
    ulong SizeInBytes { get; }

    /// <summary>
    /// Gets the immutable permitted buffer usages.
    /// </summary>
    BufferUsage Usage { get; }

    /// <summary>
    /// Gets the CPU access preference chosen at allocation.
    /// </summary>
    MemoryPreference Memory { get; }

    /// <summary>
    /// Creates a non-owning, nonempty element range within the allocation.
    /// </summary>
    /// <param name="offset">The start offset in elements, or bytes for the backend byte-range contract.</param>
    /// <param name="count">The number of elements; no implicit padding or rounding is applied.</param>
    /// <returns>The non-owning element range; the allocation lifetime is unchanged.</returns>
    BufferSlice<T> Slice(ulong offset, ulong count);

    /// <summary>
    /// Copies into idle Upload memory without GPU commands, staging allocation, or submission.
    /// </summary>
    /// <param name="source">The elements to copy; their count must not exceed the Upload range.</param>
    void CopyFrom(ReadOnlySpan<T> source);

    /// <summary>
    /// Copies completed, idle Readback memory into caller storage without GPU work or completion waits.
    /// </summary>
    /// <param name="destination">The caller storage, which must fit the complete Readback range.</param>
    void CopyTo(Span<T> destination);
}
