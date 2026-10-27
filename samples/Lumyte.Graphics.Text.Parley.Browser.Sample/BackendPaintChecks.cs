using System.Numerics;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.Graphics.Text.Parley.Browser.Sample;

internal static partial class Program
{
    private static readonly List<(int X, int Y, Vector4 Color, string Name)> _paintExpectations = [];

    private static void BuildBackendPaintChecks(DrawingCanvas2D canvas, ParleyTextEngine2D engine, ParleyTextOptions2D options)
    {
        _paintExpectations.Clear();
        var ink = PaintGraph2D.Solid(new Color2D(0.1f, 0.15f, 0.25f, 1));
        canvas.DrawText(engine.CreateLayout("COLRv1 / 28 isolated composite modes", options with { FontSize = 22 }, ink), new Vector2(36, 1000));
        var sourceColor = new Color2D(0.82f, 0.23f, 0.12f, 0.53f);
        var backdropColor = new Color2D(0.11f, 0.46f, 0.74f, 0.61f);
        var targetColor = new Color2D(0.28f, 0.13f, 0.42f, 1);
        Vector4 source = Decode(sourceColor);
        Vector4 backdrop = Decode(backdropColor);
        Vector4 target = Decode(targetColor);
        for (int mode = 0; mode < 28; mode++)
        {
            int x = 36 + ((mode % 7) * 118);
            int y = 1050 + ((mode / 7) * 80);
            var rectangle = new Rect2D(x, y, 52, 24);
            canvas.FillRect(rectangle, targetColor);
            var sourcePaint = PaintGraph2D.Clip(RectanglePath(new Rect2D(x, y, 35, 20)), PaintGraph2D.Solid(sourceColor));
            var backdropPaint = PaintGraph2D.Clip(RectanglePath(new Rect2D(x + 17, y + 4, 35, 20)), PaintGraph2D.Solid(backdropColor));
            canvas.FillRect(rectangle, PaintGraph2D.Composite(sourcePaint, backdropPaint, (CompositeMode2D)mode));
            string name = ((CompositeMode2D)mode).ToString();
            canvas.DrawText(engine.CreateLayout(name, options with { FontSize = 10 }, ink), new Vector2(x, y + 32));
            AddCompositeExpectation(x + 5, y + 5, source, Vector4.Zero, target, mode, name + " / source only");
            AddCompositeExpectation(x + 25, y + 10, source, backdrop, target, mode, name + " / intersection");
            AddCompositeExpectation(x + 45, y + 10, Vector4.Zero, backdrop, target, mode, name + " / backdrop only");
            AddCompositeExpectation(x + 5, y + 22, Vector4.Zero, Vector4.Zero, target, mode, name + " / neither");
        }

        AddBlendBoundary(0, "Dodge black / white", new Color2D(1, 1, 1, 1), new Color2D(0, 0, 0, 1), CompositeMode2D.ColorDodge);
        AddBlendBoundary(1, "Dodge white / color", new Color2D(1, 1, 1, 1), new Color2D(0.3f, 0.6f, 0.2f, 1), CompositeMode2D.ColorDodge);
        AddBlendBoundary(2, "Burn white / black", new Color2D(0, 0, 0, 1), new Color2D(1, 1, 1, 1), CompositeMode2D.ColorBurn);
        AddBlendBoundary(3, "Burn black / color", new Color2D(0, 0, 0, 1), new Color2D(0.3f, 0.6f, 0.2f, 1), CompositeMode2D.ColorBurn);
        AddBlendBoundary(4, "Gray hue", new Color2D(0.5f, 0.5f, 0.5f, 1), new Color2D(0.8f, 0.3f, 0.1f, 1), CompositeMode2D.HslHue);
        AddBlendBoundary(5, "Gray saturation", new Color2D(0.8f, 0.3f, 0.1f, 1), new Color2D(0.5f, 0.5f, 0.5f, 1), CompositeMode2D.HslSaturation);
        AddBlendBoundary(6, "Gray color", new Color2D(0.5f, 0.5f, 0.5f, 1), new Color2D(0.8f, 0.3f, 0.1f, 1), CompositeMode2D.HslColor);
        AddBlendBoundary(7, "Gray luminosity", new Color2D(0.5f, 0.5f, 0.5f, 1), new Color2D(0.5f, 0.5f, 0.5f, 1), CompositeMode2D.HslLuminosity);
        AddBlendBoundary(8, "Transparent color", new Color2D(0, 1, 0, 0), backdropColor, CompositeMode2D.HslColor);
        AddBlendBoundary(9, "Softlight black", new Color2D(1, 1, 1, 1), new Color2D(0, 0, 0, 1), CompositeMode2D.SoftLight);
        BuildGradientChecks(canvas, engine, options);

        void AddBlendBoundary(int index, string name, Color2D sourceInput, Color2D backdropInput, CompositeMode2D mode)
        {
            int x = 36 + (index * 80);
            canvas.FillRect(new Rect2D(x, 1374, 72, 10), PaintGraph2D.Composite(PaintGraph2D.Solid(sourceInput), PaintGraph2D.Solid(backdropInput), mode));
            Vector4 output = ReferenceComposite(Decode(sourceInput), Decode(backdropInput), (int)mode);
            Vector4 clear = Vector4.One;
            _paintExpectations.Add((x + 36, 1378, output + (clear * (1 - output.W)), name));
        }
    }

    private static void AddCompositeExpectation(int x, int y, Vector4 source, Vector4 backdrop, Vector4 target, int mode, string name)
    {
        Vector4 isolated = ReferenceComposite(source, backdrop, mode);
        _paintExpectations.Add((x, y, isolated + (target * (1 - isolated.W)), name));
    }

    private static void BuildGradientChecks(DrawingCanvas2D canvas, ParleyTextEngine2D engine, ParleyTextOptions2D options)
    {
        var ink = PaintGraph2D.Solid(new Color2D(0.1f, 0.15f, 0.25f, 1));
        canvas.DrawText(engine.CreateLayout("Gradient edge cases / linear-light premultiplied color", options with { FontSize = 20 }, ink), new Vector2(36, 1400));
        var red = new Color2D(1, 0, 0, 1);
        var blue = new Color2D(0, 0, 1, 1);
        Vector4 redLinear = Decode(red);
        Vector4 blueLinear = Decode(blue);
        Vector4 clear = Vector4.One;
        ColorStop2D[] stops = [new(0, red), new(1, blue)];
        ColorStop2D[] collapsedStops = [new(0.5f, red), new(0.5f, blue)];
        Place(0, "Linear light", PaintGraph2D.LinearGradient(Vector2.Zero, new Vector2(50, 0), stops), (25, 25, Vector4.Lerp(redLinear, blueLinear, 0.51f)));
        Place(1, "Premul alpha", PaintGraph2D.LinearGradient(Vector2.Zero, new Vector2(50, 0), [new(0, red), new(1, new Color2D(0, 0, 1, 0))]), (25, 25, redLinear * 0.49f));
        Place(2, "Hard stop", PaintGraph2D.LinearGradient(Vector2.Zero, new Vector2(50, 0), [new(0, red), new(0.5f, red), new(0.5f, blue), new(1, blue)]), (24, 25, redLinear), (25, 25, blueLinear));
        Place(3, "Empty stops", PaintGraph2D.LinearGradient(Vector2.Zero, new Vector2(50, 0), []), (25, 25, Vector4.Zero));
        Place(4, "Degenerate", PaintGraph2D.LinearGradient(Vector2.Zero, Vector2.Zero, Vector2.Zero, stops, ExtendMode2D.Pad), (25, 25, Vector4.Zero));
        Place(5, "Repeat span 0", PaintGraph2D.LinearGradient(Vector2.Zero, new Vector2(50, 0), collapsedStops, ExtendMode2D.Repeat), (25, 25, Vector4.Zero));
        Place(6, "Reflect span 0", PaintGraph2D.LinearGradient(Vector2.Zero, new Vector2(50, 0), collapsedStops, ExtendMode2D.Reflect), (25, 25, Vector4.Zero));
        Place(7, "Radial equal", PaintGraph2D.RadialGradient(new Vector2(25), 10, new Vector2(25), 10, stops, ExtendMode2D.Pad), (25, 25, Vector4.Zero));
        Place(8, "Sweep span 0", PaintGraph2D.SweepGradient(new Vector2(25), 1, 1, stops, ExtendMode2D.Pad), (37, 25, redLinear), (25, 37, blueLinear));
        Place(9, "Sweep repeat 0", PaintGraph2D.SweepGradient(new Vector2(25), 1, 1, stops, ExtendMode2D.Repeat), (37, 25, Vector4.Zero));
        Place(10, "Signed radius", PaintGraph2D.RadialGradient(new Vector2(25), -10, new Vector2(25), 10, stops, ExtendMode2D.Pad), (25, 25, Vector4.Lerp(redLinear, blueLinear, (10 + MathF.Sqrt(0.5f)) / 20)));
        Place(11, "Radius 0", PaintGraph2D.RadialGradient(new Vector2(25), 0, new Vector2(25), 0, stops, ExtendMode2D.Pad), (25, 25, Vector4.Zero));
        Place(12, "Single stop", PaintGraph2D.LinearGradient(Vector2.Zero, new Vector2(50, 0), [new(0.5f, blue)]), (25, 25, blueLinear));
        float rightAngle = MathF.Atan2(0.5f, 12.5f);
        Place(13, "Sweep crossing 0", PaintGraph2D.SweepGradient(new Vector2(25), -MathF.PI / 2, MathF.PI / 2, stops, ExtendMode2D.Pad), (37, 25, Vector4.Lerp(redLinear, blueLinear, (rightAngle + (MathF.PI / 2)) / MathF.PI)));

        void Place(int index, string name, PaintGraph2D paint, params (int X, int Y, Vector4 Expected)[] samples)
        {
            int x = 36 + ((index % 7) * 118);
            int y = 1450 + ((index / 7) * 80);
            canvas.FillRect(new Rect2D(x, y, 50, 50), PaintGraph2D.Transform(Matrix3x2.CreateTranslation(x, y), paint));
            canvas.DrawText(engine.CreateLayout(name, options with { FontSize = 10 }, ink), new Vector2(x, y + 54));
            foreach ((int sampleX, int sampleY, Vector4 expected) in samples)
            {
                _paintExpectations.Add((x + sampleX, y + sampleY, expected + (clear * (1 - expected.W)), name));
            }
        }
    }

    private static void CheckBackendPaintPixels(byte[] pixels)
    {
        foreach ((int x, int y, Vector4 color, string name) in _paintExpectations)
        {
            int index = ((y * (int)Width) + x) * 4;
            int[] expected = [EncodeChannel(color.X), EncodeChannel(color.Y), EncodeChannel(color.Z), (int)MathF.Round(color.W * 255)];
            for (int channel = 0; channel < 4; channel++)
            {
                if (Math.Abs(pixels[index + channel] - expected[channel]) > 4)
                {
                    throw new InvalidOperationException($"Backend pixel check failed: {name}, channel {channel}, expected {expected[channel]}, actual {pixels[index + channel]} at ({x},{y}).");
                }
            }
        }
    }

    private static Vector4 ReferenceComposite(Vector4 source, Vector4 backdrop, int mode)
    {
        float sourceAlpha = source.W;
        float backdropAlpha = backdrop.W;
        if (mode <= 12)
        {
            return mode switch
            {
                0 => Vector4.Zero,
                1 => source,
                2 => backdrop,
                3 => source + (backdrop * (1 - sourceAlpha)),
                4 => backdrop + (source * (1 - backdropAlpha)),
                5 => source * backdropAlpha,
                6 => backdrop * sourceAlpha,
                7 => source * (1 - backdropAlpha),
                8 => backdrop * (1 - sourceAlpha),
                9 => (source * backdropAlpha) + (backdrop * (1 - sourceAlpha)),
                10 => (backdrop * sourceAlpha) + (source * (1 - backdropAlpha)),
                11 => (source * (1 - backdropAlpha)) + (backdrop * (1 - sourceAlpha)),
                _ => Vector4.Min(Vector4.One, source + backdrop),
            };
        }

        var sourceRgb = new Vector3(source.X, source.Y, source.Z);
        var backdropRgb = new Vector3(backdrop.X, backdrop.Y, backdrop.Z);
        Vector3 straightSource = sourceAlpha == 0 ? Vector3.Zero : sourceRgb / sourceAlpha;
        Vector3 straightBackdrop = backdropAlpha == 0 ? Vector3.Zero : backdropRgb / backdropAlpha;
        Vector3 blended = ReferenceBlend(straightSource, straightBackdrop, mode);
        Vector3 rgb = (sourceRgb * (1 - backdropAlpha)) + (backdropRgb * (1 - sourceAlpha)) + (blended * sourceAlpha * backdropAlpha);
        return new Vector4(rgb, sourceAlpha + backdropAlpha - (sourceAlpha * backdropAlpha));
    }

    private static Vector3 ReferenceBlend(Vector3 source, Vector3 backdrop, int mode)
    {
        if (mode >= 24)
        {
            return mode switch
            {
                24 => SetLuminosity(SetSaturation(source, Saturation(backdrop)), Luminosity(backdrop)),
                25 => SetLuminosity(SetSaturation(backdrop, Saturation(source)), Luminosity(backdrop)),
                26 => SetLuminosity(source, Luminosity(backdrop)),
                _ => SetLuminosity(backdrop, Luminosity(source)),
            };
        }

        return new Vector3(ReferenceBlendChannel(source.X, backdrop.X, mode), ReferenceBlendChannel(source.Y, backdrop.Y, mode), ReferenceBlendChannel(source.Z, backdrop.Z, mode));
    }

    private static float ReferenceBlendChannel(float source, float backdrop, int mode) => mode switch
    {
        13 => source + backdrop - (source * backdrop),
        14 => backdrop <= 0.5f ? 2 * source * backdrop : 1 - (2 * (1 - source) * (1 - backdrop)),
        15 => Math.Min(source, backdrop),
        16 => Math.Max(source, backdrop),
        17 => backdrop == 0 ? 0 : source == 1 ? 1 : Math.Min(1, backdrop / (1 - source)),
        18 => backdrop == 1 ? 1 : source == 0 ? 0 : 1 - Math.Min(1, (1 - backdrop) / source),
        19 => source <= 0.5f ? 2 * source * backdrop : 1 - (2 * (1 - source) * (1 - backdrop)),
        20 => source <= 0.5f ? backdrop - ((1 - (2 * source)) * backdrop * (1 - backdrop)) : backdrop + (((2 * source) - 1) * (SoftLightCurve(backdrop) - backdrop)),
        21 => Math.Abs(backdrop - source),
        22 => source + backdrop - (2 * source * backdrop),
        _ => source * backdrop,
    };

    private static float SoftLightCurve(float value) => value <= 0.25f ? ((((16 * value) - 12) * value) + 4) * value : MathF.Sqrt(value);

    private static float Luminosity(Vector3 color) => (0.3f * color.X) + (0.59f * color.Y) + (0.11f * color.Z);

    private static float Saturation(Vector3 color) => Math.Max(color.X, Math.Max(color.Y, color.Z)) - Math.Min(color.X, Math.Min(color.Y, color.Z));

    private static Vector3 SetLuminosity(Vector3 color, float luminosity)
    {
        color += new Vector3(luminosity - Luminosity(color));
        float minimum = Math.Min(color.X, Math.Min(color.Y, color.Z));
        float maximum = Math.Max(color.X, Math.Max(color.Y, color.Z));
        if (minimum < 0)
        {
            color = new Vector3(luminosity) + ((color - new Vector3(luminosity)) * luminosity / (luminosity - minimum));
        }

        if (maximum > 1)
        {
            color = new Vector3(luminosity) + ((color - new Vector3(luminosity)) * (1 - luminosity) / (maximum - luminosity));
        }

        return color;
    }

    private static Vector3 SetSaturation(Vector3 color, float saturation)
    {
        float[] values = [color.X, color.Y, color.Z];
        int[] indices = [0, 1, 2];
        Array.Sort(indices, (left, right) => values[left].CompareTo(values[right]));
        float minimum = values[indices[0]];
        float maximum = values[indices[2]];
        values[indices[1]] = maximum > minimum ? (values[indices[1]] - minimum) * saturation / (maximum - minimum) : 0;
        values[indices[2]] = maximum > minimum ? saturation : 0;
        values[indices[0]] = 0;
        return new Vector3(values[0], values[1], values[2]);
    }

    private static Path2D RectanglePath(Rect2D rectangle)
    {
        var path = new PathBuilder2D();
        path.MoveTo(new Vector2(rectangle.X, rectangle.Y));
        path.LineTo(new Vector2(rectangle.X + rectangle.Width, rectangle.Y));
        path.LineTo(new Vector2(rectangle.X + rectangle.Width, rectangle.Y + rectangle.Height));
        path.LineTo(new Vector2(rectangle.X, rectangle.Y + rectangle.Height));
        path.Close();
        return path.Build();
    }

    private static Vector4 Decode(Color2D color) => new(DecodeChannel(color.R) * color.A, DecodeChannel(color.G) * color.A, DecodeChannel(color.B) * color.A, color.A);

    private static float DecodeChannel(float value) => value <= 0.04045f ? value / 12.92f : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);

    private static int EncodeChannel(float value) => (int)MathF.Round(Math.Clamp(value <= 0.0031308f ? value * 12.92f : (1.055f * MathF.Pow(value, 1 / 2.4f)) - 0.055f, 0, 1) * 255);
}
