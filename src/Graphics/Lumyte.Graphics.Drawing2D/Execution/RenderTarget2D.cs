using Lumyte.Graphics.Abstractions;
using Lumyte.Mathematics.Colors;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>Describes a borrowed display-linear two-dimensional rendering destination.</summary>
public sealed record RenderTarget2D
{
    private readonly SrgbRgba<float> _clearColor;

    /// <summary>Gets the whole single-mip, single-layer Rgba8Srgb or Rgba16Float attachment view.</summary>
    public required IGraphicsTextureView View { get; init; }

    /// <summary>Gets the finite positive logical-to-physical pixel scale.</summary>
    public float PixelScale { get; init; } = 1;

    /// <summary>Gets the initial attachment operation.</summary>
    public AttachmentLoadOp LoadOp { get; init; } = AttachmentLoadOp.Load;

    /// <summary>Gets or initializes the finite unit straight-alpha sRGB color used by Clear; invalid values are rejected before assignment.</summary>
    public SrgbRgba<float> ClearColor
    {
        get => _clearColor;
        init
        {
            ModelValidation2D.Color(value, nameof(ClearColor));
            _clearColor = value;
        }
    }
}
