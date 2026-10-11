using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>Describes borrowed image storage and source-relative tiling without a sampler.</summary>
public sealed record ImagePattern2D
{
    /// <summary>Gets the borrowed, single-mip, single-layer sampled D2 view.</summary>
    public required IGraphicsTextureView View { get; init; }

    /// <summary>Gets the positive, integer texel rectangle within the selected mip.</summary>
    public required Rect2D Source { get; init; }

    /// <summary>Gets the alpha representation after format decoding.</summary>
    public AlphaRepresentation2D Alpha { get; init; } = AlphaRepresentation2D.Straight;

    /// <summary>Gets the invertible transform from source-relative texels to local paint coordinates.</summary>
    public Matrix3x2 PatternToLocal { get; init; } = Matrix3x2.Identity;

    /// <summary>Gets the horizontal source-relative extension mode.</summary>
    public ImageTileMode2D TileX { get; init; } = ImageTileMode2D.Repeat;

    /// <summary>Gets the vertical source-relative extension mode.</summary>
    public ImageTileMode2D TileY { get; init; } = ImageTileMode2D.Repeat;

    /// <summary>Gets the texel filter, with tiling applied independently to every tap.</summary>
    public ImageFilter2D Filter { get; init; } = ImageFilter2D.Linear;
}
