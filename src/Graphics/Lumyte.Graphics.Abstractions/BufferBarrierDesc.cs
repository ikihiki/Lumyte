namespace Lumyte.Graphics.Abstractions;

/// <summary>Describes a buffer range dependency.</summary>
/// <typeparam name="T">The unmanaged storage element type.</typeparam>
public sealed record BufferBarrierDesc<T>
    where T : unmanaged
{
    /// <summary>Gets the buffer range.</summary>
    public required BufferSlice<T> Buffer { get; init; }

    /// <summary>Gets the preceding accesses.</summary>
    public required BarrierScope Before { get; init; }

    /// <summary>Gets the subsequent accesses.</summary>
    public required BarrierScope After { get; init; }
}
