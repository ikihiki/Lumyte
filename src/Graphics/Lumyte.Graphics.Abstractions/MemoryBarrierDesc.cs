namespace Lumyte.Graphics.Abstractions;

/// <summary>Describes a global memory dependency.</summary>
public sealed record MemoryBarrierDesc
{
    /// <summary>Gets the preceding accesses.</summary>
    public required BarrierScope Before { get; init; }

    /// <summary>Gets the subsequent accesses.</summary>
    public required BarrierScope After { get; init; }
}
