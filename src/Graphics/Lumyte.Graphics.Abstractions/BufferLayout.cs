namespace Lumyte.Graphics.Abstractions;

/// <summary>
/// Describes backend-resolved element storage and GPU copy requirements.
/// </summary>
/// <typeparam name="T">The unmanaged element type; shader ABI compatibility is validated separately.</typeparam>
public readonly struct BufferLayout<T>
    where T : unmanaged
{
    /// <summary>Initializes a new instance of the <see cref="BufferLayout{T}"/> struct without adding padding.</summary>
    /// <param name="elementSize">The host size of T in bytes.</param>
    /// <param name="stride">The raw storage stride, equal to the host element size.</param>
    /// <param name="offsetAlignment">The required GPU copy offset alignment in bytes.</param>
    /// <param name="sizeAlignment">The required GPU copy length alignment in bytes.</param>
    public BufferLayout(ulong elementSize, ulong stride, ulong offsetAlignment, ulong sizeAlignment)
    {
        if (elementSize != (ulong)System.Runtime.CompilerServices.Unsafe.SizeOf<T>())
        {
            throw new ArgumentOutOfRangeException(nameof(elementSize));
        }

        if (stride != elementSize)
        {
            throw new ArgumentOutOfRangeException(nameof(stride));
        }

        if (offsetAlignment == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offsetAlignment));
        }

        if (sizeAlignment == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeAlignment));
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
