namespace Lumyte.Graphics.Abstractions;

/// <summary>Contains owner-local materialization accounting, not driver memory or physical draw counts.</summary>
public sealed record ArgumentTablePlanStatistics
{
    /// <summary>Gets created partition tables.</summary>
    public int Tables { get; init; }

    /// <summary>Gets successful texture registrations across partitions.</summary>
    public long Textures { get; init; }

    /// <summary>Gets successful sampler registrations across partitions.</summary>
    public long Samplers { get; init; }

    /// <summary>Gets successful buffer range registrations across partitions.</summary>
    public long Buffers { get; init; }

    /// <summary>Gets requests reusing a texture registration in their selected partition.</summary>
    public long ReusedTextures { get; init; }

    /// <summary>Gets requests reusing a sampler registration in their selected partition.</summary>
    public long ReusedSamplers { get; init; }

    /// <summary>Gets requests reusing a buffer registration in their selected partition.</summary>
    public long ReusedBuffers { get; init; }

    /// <summary>Gets ordered capacity boundaries after the first table.</summary>
    public int Rollovers { get; init; }
}
