namespace Lumyte.Graphics.Abstractions;

/// <summary>Describes a single-sample two-dimensional texture with mip levels and array layers.</summary>
public sealed record TextureDesc
{
    /// <summary>Gets the positive base width in texels.</summary>
    public required uint Width { get; init; }

    /// <summary>Gets the positive base height in texels.</summary>
    public required uint Height { get; init; }

    /// <summary>Gets the exact storage format; no implicit substitution is permitted.</summary>
    public required TextureFormat Format { get; init; }

    /// <summary>Gets the nonempty set of permitted operations.</summary>
    public required TextureUsage Usage { get; init; }

    /// <summary>Gets the number of allocated mip levels; content is not generated automatically.</summary>
    public uint MipLevels { get; init; } = 1;

    /// <summary>Gets the positive array layer count, unchanged across mip levels.</summary>
    public uint ArrayLayers { get; init; } = 1;
}
