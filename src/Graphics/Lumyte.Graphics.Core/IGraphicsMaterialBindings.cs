namespace Lumyte.Graphics;

/// <summary>
/// Owns an immutable material snapshot and leases its finite resource set.
/// </summary>
public interface IGraphicsMaterialBindings : IDisposable
{
    /// <summary>
    /// Gets the reflected layout used to pack this snapshot.
    /// </summary>
    MaterialResourceLayout Layout { get; }

    /// <summary>
    /// Gets the positive number of material records.
    /// </summary>
    ulong MaterialCount { get; }

    /// <summary>
    /// Gets the complete packed snapshot size in bytes.
    /// </summary>
    ulong SizeInBytes { get; }

    /// <summary>
    /// Packs the complete snapshot into an exact-size idle Upload range; records no GPU work.
    /// </summary>
    /// <param name="destination">The caller-owned destination storage or GPU range.</param>
    void CopyTo(BufferSlice<byte> destination);
}
