namespace Lumyte.Graphics;

/// <summary>
/// Provides CPU-only material packing into caller-owned Upload storage.
/// </summary>
public static class MaterialDataTransfer
{
    /// <summary>
    /// Packs a complete material snapshot into the destination range without submitting GPU work.
    /// </summary>
    /// <param name="destination">The caller-owned destination storage or GPU range.</param>
    /// <param name="materials">The logical material snapshot, packed set, or opaque material range.</param>
    public static void CopyFrom(this BufferSlice<byte> destination, IGraphicsMaterialBindings materials)
    {
        ArgumentNullException.ThrowIfNull(materials);
        materials.CopyTo(destination);
    }
}
