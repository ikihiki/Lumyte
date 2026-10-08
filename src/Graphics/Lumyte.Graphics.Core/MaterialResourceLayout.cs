namespace Lumyte.Graphics;

/// <summary>
/// Describes backend-resolved storage and sampled-resource limits from offline Slang reflection.
/// </summary>
public sealed class MaterialResourceLayout
{
    internal MaterialResourceLayout(object handle, uint pairCapacity, ulong elementStrideInBytes)
    {
        Handle = handle;
        PairCapacity = pairCapacity;
        ElementStrideInBytes = elementStrideInBytes;
    }

    /// <summary>
    /// Gets the maximum number of sampled pairs, including fallback.
    /// </summary>
    public uint PairCapacity { get; }

    /// <summary>
    /// Gets the reflected buffer element stride in bytes; Core does not define an element schema.
    /// </summary>
    public ulong ElementStrideInBytes { get; }

    internal object Handle { get; }

    /// <summary>
    /// Computes the checked packed byte size for a positive material count.
    /// </summary>
    /// <param name="count">The number of elements; no implicit padding or rounding is applied.</param>
    /// <returns>The checked byte size for the requested count.</returns>
    public ulong GetSizeInBytes(ulong count) => count != 0 ? checked(count * ElementStrideInBytes) : throw new ArgumentOutOfRangeException(nameof(count));
}
