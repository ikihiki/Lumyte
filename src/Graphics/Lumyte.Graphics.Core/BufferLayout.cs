namespace Lumyte.Graphics;

/// <summary>
/// Describes backend-resolved element storage and GPU copy requirements.
/// </summary>
/// <typeparam name="T">The unmanaged element type; shader ABI compatibility is validated separately.</typeparam>
public readonly struct BufferLayout<T>
    where T : unmanaged
{
    internal BufferLayout(ulong elementSize, ulong stride, ulong offsetAlignment, ulong sizeAlignment)
    {
        if (elementSize == 0 || stride < elementSize || offsetAlignment == 0 || sizeAlignment == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stride));
        }

        ElementSizeInBytes = elementSize;
        ElementStrideInBytes = stride;
        CopyOffsetAlignmentInBytes = offsetAlignment;
        CopySizeAlignmentInBytes = sizeAlignment;
        CopyOffsetAlignmentInElements = offsetAlignment / Gcd(offsetAlignment, stride);
        CopyCountAlignment = sizeAlignment / Gcd(sizeAlignment, stride);
    }

    /// <summary>
    /// Gets the host size of one unmanaged element in bytes.
    /// </summary>
    public ulong ElementSizeInBytes { get; }

    /// <summary>
    /// Gets the backend storage stride of one element in bytes.
    /// </summary>
    public ulong ElementStrideInBytes { get; }

    /// <summary>
    /// Gets the byte alignment required for GPU copy offsets.
    /// </summary>
    public ulong CopyOffsetAlignmentInBytes { get; }

    /// <summary>
    /// Gets the byte alignment required for GPU copy sizes.
    /// </summary>
    public ulong CopySizeAlignmentInBytes { get; }

    /// <summary>
    /// Gets the smallest positive element offset satisfying GPU copy alignment.
    /// </summary>
    public ulong CopyOffsetAlignmentInElements { get; }

    /// <summary>
    /// Gets the smallest positive element count satisfying GPU copy size alignment.
    /// </summary>
    public ulong CopyCountAlignment { get; }

    /// <summary>
    /// Computes the checked byte size of an element count; an unresolved layout is rejected.
    /// </summary>
    /// <param name="count">The number of elements; no implicit padding or rounding is applied.</param>
    /// <returns>The checked byte size for the requested count.</returns>
    public ulong GetSizeInBytes(ulong count)
    {
        if (ElementStrideInBytes == 0)
        {
            throw new InvalidOperationException("Unresolved buffer layout.");
        }

        return checked(count * ElementStrideInBytes);
    }

    private static ulong Gcd(ulong a, ulong b)
    {
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }

        return a;
    }
}
