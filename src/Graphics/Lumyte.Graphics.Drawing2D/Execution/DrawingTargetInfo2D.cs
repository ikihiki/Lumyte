using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>Describes a destination geometry on the root drawing's physical pixel grid.</summary>
public record DrawingTargetInfo2D
{
    /// <summary>Gets the allocation's exact descriptor.</summary>
    public required TextureDesc Description { get; init; }

    /// <summary>Gets the finite positive logical-to-physical pixel scale.</summary>
    public float PixelScale { get; init; } = 1;

    /// <summary>Gets the integral root pixel coordinate corresponding to this texture's origin.</summary>
    public Vector2 PixelOrigin { get; init; }

    /// <summary>Gets the initial attachment operation.</summary>
    public AttachmentLoadOp LoadOp { get; init; } = AttachmentLoadOp.Load;

    /// <summary>Gets the straight sRGB color used by Clear.</summary>
    public Color2D ClearColor { get; init; }
}
