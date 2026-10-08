namespace Lumyte.Graphics;

/// <summary>
/// Describes the fixed material ABI validated against offline Slang reflection.
/// </summary>
public sealed class MaterialResourceLayout
{
    internal MaterialResourceLayout(object handle)
    {
        Handle = handle;
    }

    /// <summary>
    /// Gets the maximum number of sampled pairs, including fallback.
    /// </summary>
    public uint PairCapacity => 4;

    internal object Handle { get; }

    /// <summary>
    /// Computes the checked packed byte size for a positive material count.
    /// </summary>
    /// <param name="count">The number of elements; no implicit padding or rounding is applied.</param>
    /// <returns>The checked byte size for the requested count.</returns>
    public ulong GetSizeInBytes(ulong count) => count != 0 ? checked(count * 32) : throw new ArgumentOutOfRangeException(nameof(count));
}
