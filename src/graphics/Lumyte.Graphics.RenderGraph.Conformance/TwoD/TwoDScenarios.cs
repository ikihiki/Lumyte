using System.Numerics;

using Lumyte.Graphics.Text;
using Lumyte.Graphics.TwoD;

namespace Lumyte.Graphics.RenderGraph.Conformance;

public static class TwoDScenarios
{
    public static IEnumerable<string> Names => new[]
    {
        "shapes", "curves", "post-close-path", "evenodd", "affine-clip", "linear-gradient", "oblique-gradient", "radial-gradient", "sweep-gradient",
        "nearest-image", "linear-image", "srgb-image", "image-path-clip", "geometry", "prepared-text", "color-glyph", "foreground-gradient-glyph", "layered-glyph",
        "layer-opacity", "layer-blur", "layer-mask", "layer-shadow", "layer-clip-blur", "layer-mask-outside", "layer-shadow-blur", "layer-mask-shadow",
        "coverage", "sdf", "msdf", "image-brush", "empty-layer", "empty-scene", "device-scale", "singular-transform",
    }.Concat(Enum.GetNames<StrokeJoin>().Select(name => "join-" + name))
      .Concat(Enum.GetNames<StrokeCap>().Select(name => "cap-" + name))
      .Concat(new[] { "dash", "odd-dash", "closed-stroke", "closed-dash" })
      .Concat(Enum.GetNames<CompositeMode>().Select(name => "blend-" + name));

    public static Draw2DScene Create(string name)
    {
        using var draw = new Draw2DSceneBuilder(name == "device-scale" ? 1.5f : 1);
        var coral = Brush.Solid(new Color(0.8f, 0.1f, 0.2f, 0.75f));
        var blue = Brush.Solid(new Color(0.1f, 0.3f, 0.9f, 0.8f));
        var white = Brush.Solid(Color.White);
        switch (name)
        {
            case "shapes":
                draw.FillRectangle(new(4, 4, 28, 22), coral);
                draw.FillRoundedRectangle(new(24.25f, 12.5f, 34, 28), new CornerRadius(3, 8, 5, 10), blue);
                draw.FillEllipse(new(8.25f, 31.5f, 28, 25), white);
                break;
            case "curves":
                draw.DrawPath(CurvedShape(), coral);
                break;
            case "post-close-path":
                draw.DrawPath(new PathBuilder().MoveTo(new(32, 32)).LineTo(new(56, 32)).LineTo(new(56, 56)).Close()
                    .LineTo(new(8, 32)).LineTo(new(8, 8)).Close().Build(), coral);
                break;
            case "evenodd":
                draw.DrawPath(new PathBuilder().MoveTo(new(4, 4)).LineTo(new(59, 4)).LineTo(new(59, 59)).LineTo(new(4, 59)).Close()
                    .MoveTo(new(18, 18)).LineTo(new(45, 18)).LineTo(new(45, 45)).LineTo(new(18, 45)).Close().Build(), coral, FillRule.EvenOdd);
                break;
            case "affine-clip":
                draw.Transform(Matrix3x2.CreateRotation(0.23f, new(32, 32)));
                using (draw.BeginClip(new Rect(12, 12, 38, 38)))
                {
                    draw.Transform(Matrix3x2.CreateTranslation(3, -2));
                    draw.FillEllipse(new(3, 6, 53, 44), blue);
                    using (draw.BeginClip(CurvedShape()))
                    { draw.FillRectangle(new(0, 0, 64, 64), coral); }
                }
                break;
            case "linear-gradient":
                draw.FillRectangle(new(4, 4, 56, 56), Brush.LinearGradient(new(12, 0), new(38, 0), Stops(), GradientExtendMode.Reflect));
                break;
            case "oblique-gradient":
                draw.FillRectangle(new(4, 4, 56, 56), Brush.LinearGradient(new(8, 8), new(44, 20), new(20, 44), Stops()));
                break;
            case "radial-gradient":
                draw.FillEllipse(new(3, 3, 58, 58), Brush.RadialGradient(new(25, 28), 2, new(33, 32), 25, Stops(), GradientExtendMode.Pad));
                break;
            case "sweep-gradient":
                draw.FillRoundedRectangle(new(4, 4, 56, 56), 8, Brush.SweepGradient(new(32, 32), 0, MathF.Tau, Stops()));
                break;
            case "nearest-image":
                draw.DrawImage(Checker(), new(8, 8, 48, 48), sampling: new(Draw2DImageFilter.Nearest));
                break;
            case "linear-image":
                draw.DrawImage(Checker(), new(8, 8, 48, 48), new(1, 1, 6, 6), new Color(0.5f, 1, 0.8f, 0.75f));
                break;
            case "srgb-image":
                draw.DrawImage(Checker(srgb: true), new(8, 8, 48, 48));
                break;
            case "image-path-clip":
                draw.Transform(Matrix3x2.CreateRotation(-0.1f, new(32, 32)));
                using (draw.BeginClip(CurvedShape()))
                { draw.DrawImage(Checker(), new(2, 2, 60, 60)); }
                break;
            case "image-brush":
                draw.FillEllipse(new(4, 4, 56, 56), Brush.Image(Checker(), Matrix3x2.CreateScale(2.5f),
                    new(Draw2DImageFilter.Linear, Draw2DImageExtend.Repeat, Draw2DImageExtend.Mirror)));
                break;
            case "geometry":
                draw.DrawGeometry(PolygonGeometry.FromConvexPolygon([new(6, 12), new(32, 4), new(59, 20), new(47, 57), new(11, 49)]), Matrix3x2.Identity, coral);
                break;
            case "prepared-text":
                draw.DrawText(Text(), new(5, 12), coral);
                break;
            case "color-glyph":
                draw.DrawText(Text(color: true), new(5, 12), blue);
                break;
            case "foreground-gradient-glyph":
                draw.DrawText(Text(color: true, foreground: true), new(5, 12), Brush.LinearGradient(new(0, 0), new(0, 32), Stops()));
                break;
            case "layered-glyph":
                draw.DrawText(Text(color: true, layered: true), new(5, 12), blue);
                break;
            case "layer-opacity":
                draw.FillRectangle(new(3, 3, 58, 58), blue);
                using (draw.BeginLayer(new(new(4, 4, 56, 56), Opacity: 0.4f)))
                { draw.FillRectangle(new(8, 8, 36, 31), coral); draw.FillEllipse(new(22, 22, 33, 33), white); }
                break;
            case "hdr-layer":
                using (draw.BeginLayer(new(new(0, 0, 64, 64), Opacity: 0.6f, BlurRadius: 6)))
                { draw.FillRectangle(new(16, 16, 32, 32), Brush.Solid(new Color(4, 0.5f, 2, 0.75f))); }
                break;
            case "layer-blur":
                using (draw.BeginLayer(new(new(0, 0, 64, 64), BlurRadius: 6)))
                { draw.FillRectangle(new(20, 18, 24, 28), coral); }
                break;
            case "layer-mask":
                using (draw.BeginLayer(new(new(8, 8, 48, 48), Mask: new(Checker(), new(8, 8, 48, 48)))))
                { draw.FillRectangle(new(8, 8, 48, 48), coral); }
                break;
            case "layer-shadow":
            case "layer-shadow-blur":
                using (draw.BeginLayer(new(new(0, 0, 64, 64), BlurRadius: name == "layer-shadow-blur" ? 6 : 0,
                    Shadow: new(new(8, 5), new Color(0.2f, 0.6f, 0.3f, 0.7f), 6))))
                { draw.FillRectangle(new(16, 15, 24, 28), coral); }
                break;
            case "layer-mask-shadow":
                using (draw.BeginLayer(new(new(0, 0, 64, 64), BlurRadius: 3, Opacity: 0.7f,
                    Mask: new(Checker(), new(20, 18, 24, 28)), Shadow: new(new(8, 5), new Color(0.2f, 0.6f, 0.3f, 0.7f), 6))))
                { draw.FillRectangle(new(12, 12, 32, 32), coral); }
                break;
            case "layer-clip-blur":
                using (draw.BeginClip(new Rect(24, 14, 23, 35)))
                using (draw.BeginLayer(new(new(0, 0, 64, 64), BlurRadius: 6)))
                { draw.FillRectangle(new(16, 18, 24, 28), coral); }
                break;
            case "layer-mask-outside":
                using (draw.BeginLayer(new(new(0, 0, 64, 64), Mask: new(Checker(), new(20, 18, 24, 28)))))
                { draw.FillRectangle(new(4, 4, 56, 56), coral); }
                break;
            case "coverage":
            case "sdf":
            case "msdf":
                draw.DrawDistanceField(DistanceField(name), new(16, 16, 32, 32), coral);
                break;
            case "empty-layer":
                draw.FillRectangle(new(0, 0, 64, 64), blue);
                using (draw.BeginLayer(new(new(10, 10, 42, 42), CompositeMode: CompositeMode.Source)))
                { }
                break;
            case "empty-scene":
                break;
            case "singular-transform":
                using (draw.BeginState())
                {
                    draw.Transform(Matrix3x2.CreateScale(0, 1));
                    draw.FillRectangle(new(4, 4, 56, 56), coral);
                }
                draw.FillRectangle(new(20, 20, 24, 24), blue);
                break;
            case "device-scale":
                draw.FillRoundedRectangle(new(4.25f, 4.25f, 33, 31), 5, coral);
                break;
            default:
                if (name.StartsWith("blend-", StringComparison.Ordinal))
                {
                    draw.FillRectangle(new(0, 0, 64, 64), blue);
                    using (draw.BeginLayer(new(new(8, 8, 48, 48), CompositeMode: Enum.Parse<CompositeMode>(name[6..]))))
                    { draw.FillRectangle(new(8, 8, 48, 48), coral); }
                }
                else
                {
                    StrokeJoin join = name.StartsWith("join-", StringComparison.Ordinal) ? Enum.Parse<StrokeJoin>(name[5..]) : StrokeJoin.Round;
                    StrokeCap cap = name.StartsWith("cap-", StringComparison.Ordinal) ? Enum.Parse<StrokeCap>(name[4..]) : StrokeCap.Butt;
                    float[]? dashes = name is "dash" or "closed-dash" ? [6, 4] : name == "odd-dash" ? [5, 3, 2] : null;
                    var strokePath = new PathBuilder().MoveTo(new(9, 49)).LineTo(new(27, 11)).LineTo(new(52, 49));
                    if (name.StartsWith("closed-", StringComparison.Ordinal))
                    { strokePath.Close(); }
                    draw.StrokePath(strokePath.Build(), coral,
                        new(9, join, cap, miterLimit: 2, dashes: dashes, dashOffset: 2));
                }
                break;
        }
        return draw.Finish();
    }

    public static PathGeometry CurvedShape() => new PathBuilder().MoveTo(new(8, 32))
        .CubicTo(new(3, 0), new(47, 1), new(54, 23)).QuadraticTo(new(67, 57), new(31, 57))
        .CubicTo(new(11, 60), new(4, 48), new(8, 32)).Close().Build();
    private static GradientStop[] Stops() => [new(0, new Color(1, 0, 0, 1)), new(0.4f, new Color(0, 1, 0, 0.6f)), new(1, new Color(0, 0.2f, 1, 1))];
    public static DistanceFieldUploadData DistanceField(string name)
    {
        const int size = 32;
        var kind = name switch { "coverage" => DistanceFieldKind.Coverage, "sdf" => DistanceFieldKind.Sdf, _ => DistanceFieldKind.Msdf };
        byte[] pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = 11 - Vector2.Distance(new(x + 0.5f, y + 0.5f), new(16, 16));
                float value = Math.Clamp(0.5f + distance / (kind == DistanceFieldKind.Coverage ? 1 : 8), 0, 1);
                int offset = (y * size + x) * 4;
                pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = (byte)MathF.Round(value * 255);
                if (kind == DistanceFieldKind.Msdf)
                {
                    pixels[offset] = (byte)Math.Clamp(pixels[offset] + 9, 0, 255);
                    pixels[offset + 2] = (byte)Math.Clamp(pixels[offset + 2] - 13, 0, 255);
                }
                pixels[offset + 3] = 255;
            }
        }
        var image = new GpuImageUploadData(new(name + "-pixels", 1), new(size, size, GpuFormat.Rgba8Unorm),
            GpuImageColorEncoding.Data, GpuImageAlphaMode.Opaque, [new(0, 0, size * 4, size * size * 4, pixels)]);
        return new(new(name, 1), image, kind, new(size, size), kind == DistanceFieldKind.Coverage ? 0 : 8);
    }
    public static GpuImageUploadData Checker(bool srgb = false)
    {
        byte[] pixels = new byte[8 * 8 * 4];
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                int offset = (y * 8 + x) * 4;
                pixels[offset] = (byte)((x / 2 + y / 2) % 2 == 0 ? 224 : 32);
                pixels[offset + 1] = (byte)(y * 28);
                pixels[offset + 2] = 128;
                pixels[offset + 3] = (byte)(x < 4 ? 255 : 128);
            }
        }
        return new(new(srgb ? "two-d-checker-srgb" : "two-d-checker", 1), new(8, 8, GpuFormat.Rgba8Unorm),
            srgb ? GpuImageColorEncoding.Srgb : GpuImageColorEncoding.Linear, GpuImageAlphaMode.Straight, [new(0, 0, 32, 256, pixels)]);
    }
    public static TextDrawData Text(bool color = false, bool foreground = false, bool layered = false)
    {
        var outline = new PathBuilder().MoveTo(new(1, 32)).LineTo(new(11, 0)).LineTo(new(16, 0)).LineTo(new(26, 32))
            .LineTo(new(20, 32)).LineTo(new(17, 23)).LineTo(new(10, 23)).LineTo(new(7, 32)).Close()
            .MoveTo(new(11, 18)).LineTo(new(16, 18)).LineTo(new(13.5f, 8)).Close().Build();
        GlyphPaintNode? paint = color ? new GlyphPaintNode.Outline(outline, FillRule.EvenOdd,
            new GlyphPaintNode.Gradient(Brush.LinearGradient(new(0, 0), new(25, 32), Stops()))) : null;
        if (foreground)
        {
            paint = new GlyphPaintNode.Outline(outline, FillRule.EvenOdd,
                GlyphPaintNode.LinearGradient(new(0, 0), new(25, 0), [new(0, new Color(0.7f, 0.2f, 0.1f, 0.8f)), GlyphGradientStop.Foreground(1, 0.7f)]));
        }
        if (layered)
        {
            paint = new GlyphPaintNode.Outline(outline, FillRule.EvenOdd, new GlyphPaintNode.Layers([
                new GlyphPaintNode.Solid(new Color(0.1f, 0.2f, 0.9f, 0.55f)),
                new GlyphPaintNode.Solid(new Color(0.8f, 0.2f, 0.1f, 0.6f))]));
        }
        var glyph = new GlyphUploadData(new(layered ? "layered-outline" : foreground ? "foreground-outline" : color ? "color-outline" : "outline", 1), new(1, 0, 25, 32), new(outline, FillRule.EvenOdd), paint);
        return new(new("prepared-glyph-run", layered ? 4 : foreground ? 3 : color ? 2 : 1), new(54, 32), [new(32, 32, 0, new(0, 0, 54, 32), new(1, 0, 52, 32))],
            [new([new(glyph, Matrix3x2.Identity, new(27, 0), 0), new(glyph, Matrix3x2.CreateTranslation(27, 0), new(27, 0), 1)])]);
    }
}
