using Lumyte.Graphics.Drawing2D;
using Lumyte.Mathematics.Colors;

namespace Lumyte.Maps;

/// <summary>An immutable ordered paint layer for a named MVT layer.</summary>
public sealed class MapStyleLayer
{
    /// <summary>Initializes a new instance of the <see cref="MapStyleLayer"/> class.</summary>
    /// <param name="sourceLayer">Schema layer name.</param>
    /// <param name="color">Straight-alpha sRGB paint.</param>
    /// <param name="stroke">Stroke settings; null fills polygons.</param>
    public MapStyleLayer(string sourceLayer, SRgba<float> color, StrokeStyle2D? stroke = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceLayer);
        _ = PaintGraph2D.Solid(color);
        if (stroke is not null && (!float.IsFinite(stroke.Width) || stroke.Width <= 0
            || !float.IsFinite(stroke.MiterLimit) || stroke.MiterLimit < 1
            || !Enum.IsDefined(stroke.Cap) || !Enum.IsDefined(stroke.Join)))
        {
            throw new ArgumentOutOfRangeException(nameof(stroke));
        }

        SourceLayer = sourceLayer;
        Color = color;
        Stroke = stroke;
    }

    /// <summary>Gets the source layer name.</summary>
    public string SourceLayer { get; }

    /// <summary>Gets the paint.</summary>
    public SRgba<float> Color { get; }

    /// <summary>Gets the immutable stroke, or null for fills.</summary>
    public StrokeStyle2D? Stroke { get; }
}
