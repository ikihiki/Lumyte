namespace Lumyte.Graphics.Drawing2D;

/// <summary>Masks child paint with a multi-channel signed distance field.</summary>
/// <param name="Pattern">The borrowed atlas region and source-texel to local transform.</param>
/// <param name="DistanceRange">The total encoded distance range in source texels, with the contour at 0.5.</param>
/// <param name="Mode">The distance channel interpretation.</param>
/// <param name="Child">The paint masked by reconstructed coverage.</param>
public sealed record DistanceFieldPaintNode2D(ImagePattern2D Pattern, float DistanceRange, DistanceFieldMode2D Mode, PaintNode2D Child) : PaintNode2D;
