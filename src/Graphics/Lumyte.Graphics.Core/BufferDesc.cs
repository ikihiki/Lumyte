namespace Lumyte.Graphics;
/// <summary>
/// Describes a typed buffer allocation.
/// </summary>
/// <typeparam name="T">The unmanaged element type; shader ABI compatibility is validated separately.</typeparam>
public sealed record BufferDesc<T>
    where T : unmanaged
{
    /// <summary>
    /// Gets the positive element count; byte size is checked without rounding.
    /// </summary>
    public required ulong Count { get; init; }

    /// <summary>
    /// Gets the required buffer operation flags.
    /// </summary>
    public required BufferUsage Usage { get; init; }

    /// <summary>
    /// Gets the memory preference; defaults to automatic device-selected storage.
    /// </summary>
    public MemoryPreference Memory { get; init; }
}
