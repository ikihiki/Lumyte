namespace Lumyte.Graphics;

/// <summary>
/// Selects allocation and CPU access requirements.
/// </summary>
public enum MemoryPreference
{
    /// <summary>
    /// Uses device-selected memory without public CPU mapping.
    /// </summary>
    Automatic,

    /// <summary>
    /// Allows completed GPU copy data to be read through CopyTo.
    /// </summary>
    Readback,

    /// <summary>
    /// Allows idle CPU writes through CopyFrom before explicit GPU copies.
    /// </summary>
    Upload,
}
