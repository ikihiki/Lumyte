using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Mathematics.Colors;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>Describes a destination geometry on the root drawing's physical pixel grid.</summary>
public record DrawingTargetInfo2D
{
    private readonly Rgba<float> _clearColor;

    /// <summary>Gets the allocation's exact descriptor.</summary>
    public required TextureDesc Description { get; init; }

    /// <summary>Gets the finite positive logical-to-physical pixel scale.</summary>
    public float PixelScale { get; init; } = 1;

    /// <summary>Gets the integral root pixel coordinate corresponding to this texture's origin.</summary>
    public Vector2 PixelOrigin { get; init; }

    /// <summary>Gets the initial attachment operation.</summary>
    public AttachmentLoadOp LoadOp { get; init; } = AttachmentLoadOp.Load;

    /// <summary>Gets or initializes the finite unit straight-alpha sRGB color used by Clear; invalid values are rejected before assignment.</summary>
    public Rgba<float> ClearColor
    {
        get => _clearColor;
        init
        {
            ModelValidation2D.Color(value, nameof(ClearColor));
            _clearColor = value;
        }
    }
}
