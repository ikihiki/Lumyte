namespace Lumyte.Graphics;

/// <summary>
/// Specifies independent logical registration capacities, not simultaneous shader binding limits.
/// </summary>
public sealed record ArgumentTableDesc
{
    /// <summary>Gets the optional diagnostic label.</summary>
    public string? Label { get; init; }

    /// <summary>Gets the number of texture registration slots.</summary>
    public uint TextureCapacity { get; init; }

    /// <summary>Gets the number of sampler registration slots.</summary>
    public uint SamplerCapacity { get; init; }

    /// <summary>Gets the number of buffer registration slots.</summary>
    public uint BufferCapacity { get; init; }
}
