namespace Lumyte.Graphics;

/// <summary>Numeric layout and copy requirements resolved by the active backend for T.</summary>
public readonly struct BufferLayout<T> where T : unmanaged
{
    public ulong ElementSizeInBytes { get; }
    public ulong ElementStrideInBytes { get; }
    public ulong CopyOffsetAlignmentInBytes { get; }
    public ulong CopySizeAlignmentInBytes { get; }
    /// <summary>Smallest positive element offset producing an aligned GPU copy offset.</summary>
    public ulong CopyOffsetAlignmentInElements { get; }
    /// <summary>Smallest positive element count producing an aligned GPU copy size.</summary>
    public ulong CopyCountAlignment { get; }
    internal BufferLayout(ulong elementSize, ulong stride, ulong offsetAlignment, ulong sizeAlignment)
    {
        if (elementSize == 0 || stride < elementSize || offsetAlignment == 0 || sizeAlignment == 0)
            throw new ArgumentOutOfRangeException(nameof(stride));
        ElementSizeInBytes = elementSize;
        ElementStrideInBytes = stride;
        CopyOffsetAlignmentInBytes = offsetAlignment;
        CopySizeAlignmentInBytes = sizeAlignment;
        CopyOffsetAlignmentInElements = offsetAlignment / Gcd(offsetAlignment, stride);
        CopyCountAlignment = sizeAlignment / Gcd(sizeAlignment, stride);
    }
    public ulong GetSizeInBytes(ulong count)
    {
        if (ElementStrideInBytes == 0) throw new InvalidOperationException("Unresolved buffer layout.");
        return checked(count * ElementStrideInBytes);
    }
    private static ulong Gcd(ulong a, ulong b)
    {
        while (b != 0) (a, b) = (b, a % b);
        return a;
    }
}
