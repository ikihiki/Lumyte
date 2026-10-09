namespace Lumyte.Graphics.Abstractions;

/// <summary>Describes one blend equation.</summary>
public sealed record BlendComponentDesc
{
    /// <summary>Gets the source factor.</summary>
    public BlendFactor Source { get; init; } = BlendFactor.One;

    /// <summary>Gets the destination factor.</summary>
    public BlendFactor Destination { get; init; } = BlendFactor.Zero;

    /// <summary>Gets the blend operation.</summary>
    public BlendOperation Operation { get; init; } = BlendOperation.Add;
}
