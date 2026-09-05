using System.Diagnostics.CodeAnalysis;
using System.Numerics;

using Lumyte.Graphics.TwoD;

namespace Lumyte.Graphics.Text;

/// <summary>
/// Shapes text and records glyphs through color bitmap, coverage, distance-field, polygon,
/// or GPU path routes. Keep the renderer alive until every prepared display list using it has
/// completed GPU execution.
/// </summary>
public sealed class TextRenderer : IDisposable
{
    private const uint MinimumAtlasSize = 64;

    private readonly Renderer renderer;
    private readonly IGpuBackend backend;
    private readonly uint atlasWidth;
    private readonly uint atlasHeight;
    private readonly Dictionary<DistanceFieldKey, CachedDistanceField> distanceFields = [];
    private readonly Dictionary<PolygonKey, PolygonGeometry?> polygons = [];
    private readonly Dictionary<ColorBitmapKey, CachedColorBitmap> colorBitmaps = [];
    private DistanceFieldAtlas? singleChannelAtlas;
    private DistanceFieldRasterizer? singleChannelRasterizer;
    private DistanceFieldAtlas? multiChannelAtlas;
    private DistanceFieldRasterizer? multiChannelRasterizer;
    private SamplerId colorBitmapSampler;
    private bool disposed;

    public TextRenderer(
        Renderer renderer,
        TextRenderingPolicy? policy = null,
        uint atlasWidth = 2048,
        uint atlasHeight = 2048)
    {
        this.renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        backend = renderer.Backend;
        Policy = policy ?? TextRenderingPolicy.Default;
        Policy.Validate();
        if (atlasWidth < MinimumAtlasSize || atlasHeight < MinimumAtlasSize)
        {
            throw new ArgumentOutOfRangeException(nameof(atlasWidth), "Text atlases must be at least 64 pixels in each dimension.");
        }
        this.atlasWidth = atlasWidth;
        this.atlasHeight = atlasHeight;
    }

    public TextRenderingPolicy Policy { get; }

    /// <summary>
    /// Shapes and records one text run. <paramref name="baseline"/> and <paramref name="fontSize"/>
    /// are expressed in logical pixels before <see cref="TextDrawOptions.Transform"/>.
    /// </summary>
    public TextDrawResult DrawText(
        CommandEncoder encoder,
        FontFace font,
        string text,
        Vector2 baseline,
        float fontSize,
        Brush brush,
        TextDrawOptions? options = null)
    {
        VerifyAlive();
        ArgumentNullException.ThrowIfNull(encoder);
        VerifyEncoder(encoder);
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(text);
        if (!float.IsFinite(baseline.X) || !float.IsFinite(baseline.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(baseline));
        }
        if (!float.IsFinite(fontSize) || fontSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fontSize));
        }

        options ??= new();
        options.Validate();
        return DrawText(encoder, font.Shape(text), baseline, fontSize, brush, options);
    }

    /// <summary>Records a previously shaped run without invoking HarfBuzz again.</summary>
    public TextDrawResult DrawText(
        CommandEncoder encoder,
        ShapedText text,
        Vector2 baseline,
        float fontSize,
        Brush brush,
        TextDrawOptions? options = null)
    {
        VerifyAlive();
        ArgumentNullException.ThrowIfNull(encoder);
        VerifyEncoder(encoder);
        ArgumentNullException.ThrowIfNull(text);
        if (!float.IsFinite(baseline.X) || !float.IsFinite(baseline.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(baseline));
        }
        if (!float.IsFinite(fontSize) || fontSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fontSize));
        }

        options ??= new();
        options.Validate();
        FontFace font = text.Owner;
        float effectiveSize = Policy.EffectiveSize(fontSize, options.Transform, options.DeviceScale);
        TextRenderingMode mode = options.RenderingMode == TextRenderingMode.Auto
            ? Policy.Select(fontSize, options.Transform, options.DeviceScale)
            : options.RenderingMode;
        ReadOnlyMemory<Color> colorPalette = default;
        bool useColorGlyphs = options.ColorGlyphMode == ColorGlyphMode.Auto
            && font.HasColorGlyphs;
        bool usePaintGlyphs = useColorGlyphs && font.HasColorPaintGlyphs;
        bool useLayerGlyphs = useColorGlyphs
            && font.HasColorLayerGlyphs
            && font.ColorPaletteCount != 0;
        if (useLayerGlyphs)
        {
            colorPalette = font.GetColorPalette(options.ColorPaletteIndex);
        }
        float scale = fontSize / font.UnitsPerEm;
        Vector2 pen = baseline;
        int fallbackCount = 0;
        int colorGlyphCount = 0;
        int bitmapGlyphCount = 0;
        uint bitmapPixelsPerEm = checked((uint)Math.Clamp(
            MathF.Ceiling(effectiveSize),
            1,
            ushort.MaxValue));

        encoder.Save();
        try
        {
            encoder.Transform(options.Transform);
            foreach (ShapedGlyph glyph in text.Glyphs.Span)
            {
                var origin = new Vector2(
                    pen.X + glyph.XOffset * scale,
                    pen.Y - glyph.YOffset * scale);
                bool drewColorGlyph = false;
                if (usePaintGlyphs
                    && font.TryGetColorPaintGlyph(
                        glyph.GlyphId,
                        options.ColorPaletteIndex,
                        out ColorPaintGlyph? paintGlyph)
                    && TryDrawColorPaintGlyph(
                        encoder,
                        paintGlyph,
                        origin,
                        scale,
                        brush))
                {
                    colorGlyphCount++;
                    drewColorGlyph = true;
                }
                if (!drewColorGlyph
                    && useLayerGlyphs
                    && font.TryGetColorGlyph(glyph.GlyphId, out ColorGlyph? colorGlyph)
                    && TryDrawColorGlyph(
                        encoder,
                        font,
                        colorGlyph,
                        colorPalette.Span,
                        origin,
                        scale,
                        effectiveSize,
                        brush,
                        mode,
                        options,
                        out int colorFallbackCount))
                {
                    fallbackCount += colorFallbackCount;
                    colorGlyphCount++;
                    drewColorGlyph = true;
                }
                if (!drewColorGlyph
                    && useColorGlyphs
                    && font.TryGetColorBitmapGlyph(
                        glyph.GlyphId,
                        bitmapPixelsPerEm,
                        out ColorBitmapGlyph? bitmapGlyph))
                {
                    DrawColorBitmap(
                        encoder,
                        font,
                        glyph.GlyphId,
                        bitmapGlyph,
                        origin,
                        scale);
                    colorGlyphCount++;
                    bitmapGlyphCount++;
                    drewColorGlyph = true;
                }
                if (!drewColorGlyph && font.TryGetGlyphPath(glyph.GlyphId, out PathGeometry? path))
                {
                    fallbackCount += DrawGlyph(
                        encoder,
                        font,
                        glyph.GlyphId,
                        path,
                        origin,
                        scale,
                        effectiveSize,
                        brush,
                        mode,
                        options);
                }

                pen.X += glyph.XAdvance * scale;
                pen.Y -= glyph.YAdvance * scale;
            }
        }
        finally
        {
            encoder.Restore();
        }

        return new(
            pen - baseline,
            text.Glyphs.Length,
            mode,
            effectiveSize,
            fallbackCount,
            colorGlyphCount,
            bitmapGlyphCount);
    }

    private static bool TryDrawColorPaintGlyph(
        CommandEncoder encoder,
        ColorPaintGlyph glyph,
        Vector2 origin,
        float scale,
        Brush foreground)
    {
        if (!ValidateColorPaintGlyph(glyph, foreground))
        {
            return false;
        }

        Matrix3x2 placement = new(scale, 0, 0, scale, origin.X, origin.Y);
        Matrix3x2 currentTransform = Matrix3x2.Identity;
        var transforms = new Stack<Matrix3x2>();
        var clips = new Stack<PaintClip>();
        var groups = new Stack<CompositeMode>();

        for (int index = 0; index < glyph.Operations.Count; index++)
        {
            switch (glyph.Operations[index])
            {
                case ColorPaintPushTransform pushTransform:
                    transforms.Push(currentTransform);
                    // HarfBuzz uses column-vector CTM multiplication (current * nested).
                    // Matrix3x2 uses row vectors, so the equivalent product is reversed.
                    currentTransform = pushTransform.Transform * currentTransform;
                    break;

                case ColorPaintPopTransform:
                    currentTransform = transforms.Pop();
                    break;

                case ColorPaintPushClipGlyph pushGlyph:
                    PushPaintClip(
                        encoder,
                        clips,
                        pushGlyph.Path,
                        currentTransform,
                        placement);
                    break;

                case ColorPaintPushClipRectangle pushRectangle:
                    if (pushRectangle.Rectangle.Width <= 0
                        || pushRectangle.Rectangle.Height <= 0)
                    {
                        PushEmptyPaintClip(clips);
                    }
                    else
                    {
                        PushPaintClip(
                            encoder,
                            clips,
                            RectanglePath(pushRectangle.Rectangle),
                            currentTransform,
                            placement);
                    }
                    break;

                case ColorPaintPopClip:
                    PaintClip poppedClip = clips.Pop();
                    if (poppedClip.IsRecorded)
                    {
                        encoder.PopClip();
                    }
                    break;

                case ColorPaintPushGroup pushGroup:
                    CompositeMode mode = (CompositeMode)(pushGroup.CompositeMode
                        ?? FindGroupMode(glyph.Operations, index));
                    encoder.PushLayer(new() { CompositeMode = mode });
                    groups.Push(mode);
                    break;

                case ColorPaintPopGroup:
                    encoder.PopLayer();
                    groups.Pop();
                    break;

                case ColorPaintSolid solid:
                    if (clips.Peek().Bounds is Rect solidBounds)
                    {
                        FillPaintClip(
                            encoder,
                            solidBounds,
                            currentTransform,
                            placement,
                            ResolveSolidBrush(solid.Color, foreground));
                    }
                    break;

                case ColorPaintLinearGradient linear:
                    if (linear.Gradient.Stops.Count != 0
                        && clips.Peek().Bounds is Rect linearBounds)
                    {
                        FillPaintClip(
                            encoder,
                            linearBounds,
                            currentTransform,
                            placement,
                            Brush.LinearGradient(
                                linear.Point0,
                                linear.Point1,
                                linear.Point2,
                                ResolveStops(linear.Gradient, foreground),
                                (GradientExtendMode)linear.Gradient.ExtendMode));
                    }
                    break;

                case ColorPaintRadialGradient radial:
                    if (radial.Gradient.Stops.Count != 0
                        && clips.Peek().Bounds is Rect radialBounds)
                    {
                        FillPaintClip(
                            encoder,
                            radialBounds,
                            currentTransform,
                            placement,
                            Brush.RadialGradient(
                                radial.Center0,
                                radial.Radius0,
                                radial.Center1,
                                radial.Radius1,
                                ResolveStops(radial.Gradient, foreground),
                                (GradientExtendMode)radial.Gradient.ExtendMode));
                    }
                    break;

                case ColorPaintSweepGradient sweep:
                    if (sweep.Gradient.Stops.Count != 0
                        && clips.Peek().Bounds is Rect sweepBounds)
                    {
                        FillPaintClip(
                            encoder,
                            sweepBounds,
                            currentTransform,
                            placement,
                            Brush.SweepGradient(
                                sweep.Center,
                                sweep.StartAngle,
                                sweep.EndAngle,
                                ResolveStops(sweep.Gradient, foreground),
                                (GradientExtendMode)sweep.Gradient.ExtendMode));
                    }
                    break;
            }
        }

        return true;
    }

    internal static bool ValidateColorPaintGlyph(ColorPaintGlyph glyph, Brush foreground)
    {
        if (glyph.Operations.Count == 0)
        {
            return false;
        }

        int transformDepth = 0;
        int clipDepth = 0;
        bool hasPaint = false;
        var groupModes = new Stack<CompositeMode>();
        try
        {
            for (int index = 0; index < glyph.Operations.Count; index++)
            {
                switch (glyph.Operations[index])
                {
                    case ColorPaintPushTransform:
                        transformDepth++;
                        break;
                    case ColorPaintPopTransform:
                        if (transformDepth-- <= 0)
                        {
                            return false;
                        }
                        break;
                    case ColorPaintPushClipGlyph:
                    case ColorPaintPushClipRectangle:
                        clipDepth++;
                        break;
                    case ColorPaintPopClip:
                        if (clipDepth-- <= 0)
                        {
                            return false;
                        }
                        break;
                    case ColorPaintPushGroup push:
                        CompositeMode pushMode = (CompositeMode)(push.CompositeMode
                            ?? FindGroupMode(glyph.Operations, index));
                        if (!IsSupportedCompositeMode(pushMode))
                        {
                            return false;
                        }
                        groupModes.Push(pushMode);
                        break;
                    case ColorPaintPopGroup pop:
                        CompositeMode popMode = (CompositeMode)pop.CompositeMode;
                        if (!IsSupportedCompositeMode(popMode)
                            || !groupModes.TryPop(out CompositeMode pushed)
                            || pushed != popMode)
                        {
                            return false;
                        }
                        break;
                    case ColorPaintSolid solid:
                        if (clipDepth == 0) { return false; }
                        hasPaint = true;
                        ResolveSolidBrush(solid.Color, foreground).Validate();
                        break;
                    case ColorPaintLinearGradient linear:
                        if (clipDepth == 0) { return false; }
                        hasPaint = true;
                        if (linear.Gradient.Stops.Count != 0)
                        {
                            Brush.LinearGradient(
                                linear.Point0,
                                linear.Point1,
                                linear.Point2,
                                ResolveStops(linear.Gradient, foreground),
                                (GradientExtendMode)linear.Gradient.ExtendMode).Validate();
                        }
                        break;
                    case ColorPaintRadialGradient radial:
                        if (clipDepth == 0) { return false; }
                        hasPaint = true;
                        if (radial.Gradient.Stops.Count != 0)
                        {
                            Brush.RadialGradient(
                                radial.Center0,
                                radial.Radius0,
                                radial.Center1,
                                radial.Radius1,
                                ResolveStops(radial.Gradient, foreground),
                                (GradientExtendMode)radial.Gradient.ExtendMode).Validate();
                        }
                        break;
                    case ColorPaintSweepGradient sweep:
                        if (clipDepth == 0) { return false; }
                        hasPaint = true;
                        if (sweep.Gradient.Stops.Count != 0)
                        {
                            Brush.SweepGradient(
                                sweep.Center,
                                sweep.StartAngle,
                                sweep.EndAngle,
                                ResolveStops(sweep.Gradient, foreground),
                                (GradientExtendMode)sweep.Gradient.ExtendMode).Validate();
                        }
                        break;
                    case null:
                        return false;
                    default:
                        return false;
                }
            }
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        return hasPaint && transformDepth == 0 && clipDepth == 0 && groupModes.Count == 0;
    }

    private static ColorPaintCompositeMode FindGroupMode(
        IReadOnlyList<ColorPaintOperation> operations,
        int pushIndex)
    {
        int depth = 0;
        for (int index = pushIndex + 1; index < operations.Count; index++)
        {
            switch (operations[index])
            {
                case ColorPaintPushGroup:
                    depth++;
                    break;
                case ColorPaintPopGroup pop when depth == 0:
                    return pop.CompositeMode;
                case ColorPaintPopGroup:
                    depth--;
                    break;
            }
        }
        throw new InvalidOperationException("A COLRv1 paint group is not balanced.");
    }

    private static void PushPaintClip(
        CommandEncoder encoder,
        Stack<PaintClip> clips,
        PathGeometry? path,
        Matrix3x2 transform,
        Matrix3x2 placement)
    {
        if (path is null)
        {
            PushEmptyPaintClip(clips);
            return;
        }

        Rect transformedBounds = TransformBounds(path.Bounds, transform);
        if (transformedBounds.Width <= 0 || transformedBounds.Height <= 0)
        {
            PushEmptyPaintClip(clips);
            return;
        }

        Rect? bounds = clips.TryPeek(out PaintClip parent)
            ? parent.Bounds is Rect parentBounds
                ? Intersect(parentBounds, transformedBounds)
                : null
            : transformedBounds;
        if (bounds is null)
        {
            PushEmptyPaintClip(clips);
            return;
        }

        clips.Push(new(bounds.Value, true));
        encoder.PushClip(path, transform * placement);
    }

    private static void PushEmptyPaintClip(Stack<PaintClip> clips)
        => clips.Push(new(null, false));

    private static bool IsSupportedCompositeMode(CompositeMode mode)
        => Enum.IsDefined(mode);

    private static void FillPaintClip(
        CommandEncoder encoder,
        Rect rootBounds,
        Matrix3x2 transform,
        Matrix3x2 placement,
        Brush brush)
    {
        if (!Matrix3x2.Invert(transform, out Matrix3x2 inverse))
        {
            return;
        }

        Rect localBounds = TransformBounds(rootBounds, inverse);
        if (localBounds.Width <= float.Epsilon || localBounds.Height <= float.Epsilon)
        {
            return;
        }
        encoder.DrawPath(
            RectanglePath(localBounds),
            transform * placement,
            brush,
            FillRule.NonZero);
    }

    private static Brush ResolveSolidBrush(ColorPaintColor paint, Brush foreground)
        => paint.IsForeground
            ? WithOpacity(foreground, paint.Color.Alpha)
            : Brush.Solid(paint.Color);

    private static GradientStop[] ResolveStops(ColorPaintGradient gradient, Brush foreground)
    {
        var stops = new GradientStop[gradient.Stops.Count];
        for (int index = 0; index < stops.Length; index++)
        {
            ColorPaintGradientStop stop = gradient.Stops[index];
            Color color = stop.Color.IsForeground
                ? WithOpacity(foreground.Color, stop.Color.Color.Alpha)
                : stop.Color.Color;
            stops[index] = new(stop.Offset, color);
        }
        return stops;
    }

    private static Brush WithOpacity(Brush brush, float opacity)
    {
        if (opacity >= 1) { return brush; }
        if (brush.Kind == BrushKind.Solid)
        {
            return Brush.Solid(WithOpacity(brush.Color, opacity));
        }

        GradientStop[] stops = brush.GradientStops
            .Select(stop => stop with { Color = WithOpacity(stop.Color, opacity) })
            .ToArray();
        return brush.Kind switch
        {
            BrushKind.LinearGradient => Brush.LinearGradient(
                brush.Point0,
                brush.Point1,
                brush.Point2,
                stops,
                brush.ExtendMode),
            BrushKind.RadialGradient => Brush.RadialGradient(
                brush.Point0,
                brush.Radius0,
                brush.Point1,
                brush.Radius1,
                stops,
                brush.ExtendMode),
            BrushKind.SweepGradient => Brush.SweepGradient(
                brush.Point0,
                brush.StartAngle,
                brush.EndAngle,
                stops,
                brush.ExtendMode),
            _ => throw new ArgumentOutOfRangeException(nameof(brush)),
        };
    }

    private static Color WithOpacity(Color color, float opacity)
        => color with { Alpha = color.Alpha * opacity };

    private static PathGeometry RectanglePath(Rect rectangle)
        => new PathBuilder()
            .MoveTo(new(rectangle.X, rectangle.Y))
            .LineTo(new(rectangle.Right, rectangle.Y))
            .LineTo(new(rectangle.Right, rectangle.Bottom))
            .LineTo(new(rectangle.X, rectangle.Bottom))
            .Close()
            .Build();

    private static Rect TransformBounds(Rect rectangle, Matrix3x2 transform)
    {
        Vector2 first = Vector2.Transform(new(rectangle.X, rectangle.Y), transform);
        Vector2 second = Vector2.Transform(new(rectangle.Right, rectangle.Y), transform);
        Vector2 third = Vector2.Transform(new(rectangle.Right, rectangle.Bottom), transform);
        Vector2 fourth = Vector2.Transform(new(rectangle.X, rectangle.Bottom), transform);
        float left = MathF.Min(MathF.Min(first.X, second.X), MathF.Min(third.X, fourth.X));
        float top = MathF.Min(MathF.Min(first.Y, second.Y), MathF.Min(third.Y, fourth.Y));
        float right = MathF.Max(MathF.Max(first.X, second.X), MathF.Max(third.X, fourth.X));
        float bottom = MathF.Max(MathF.Max(first.Y, second.Y), MathF.Max(third.Y, fourth.Y));
        return new(left, top, right - left, bottom - top);
    }

    private static Rect? Intersect(Rect left, Rect right)
    {
        float x = MathF.Max(left.X, right.X);
        float y = MathF.Max(left.Y, right.Y);
        float maximumX = MathF.Min(left.Right, right.Right);
        float maximumY = MathF.Min(left.Bottom, right.Bottom);
        return maximumX <= x || maximumY <= y
            ? null
            : new(x, y, maximumX - x, maximumY - y);
    }

    public void Dispose()
    {
        if (disposed) { return; }
        singleChannelRasterizer?.Dispose();
        multiChannelRasterizer?.Dispose();
        singleChannelAtlas?.Dispose();
        multiChannelAtlas?.Dispose();
        foreach (CachedColorBitmap bitmap in colorBitmaps.Values)
        {
            renderer.UnregisterImage(bitmap.Image);
            bitmap.Texture.Dispose();
        }
        if (!colorBitmapSampler.IsNull)
        {
            backend.DestroySampler(colorBitmapSampler);
            colorBitmapSampler = default;
        }
        distanceFields.Clear();
        polygons.Clear();
        colorBitmaps.Clear();
        disposed = true;
    }

    private void DrawColorBitmap(
        CommandEncoder encoder,
        FontFace font,
        uint glyphId,
        ColorBitmapGlyph glyph,
        Vector2 origin,
        float scale)
    {
        var key = new ColorBitmapKey(font, glyphId, glyph.Width, glyph.Height, glyph.ContentId);
        if (!colorBitmaps.TryGetValue(key, out CachedColorBitmap cached))
        {
            colorBitmapSampler = colorBitmapSampler.IsNull
                ? backend.CreateSampler(new(
                    GpuSamplerFilter.Linear,
                    GpuSamplerFilter.Linear,
                    GpuSamplerAddressMode.ClampToEdge,
                    GpuSamplerAddressMode.ClampToEdge))
                : colorBitmapSampler;
            ColorBitmapTexture texture = ColorBitmapTexture.Create(
                backend,
                glyph.Width,
                glyph.Height,
                glyph.Pixels.Span);
            try
            {
                ImageId image = renderer.RegisterImage(
                    texture.Texture,
                    texture.Description,
                    colorBitmapSampler);
                cached = new(image, texture);
                colorBitmaps.Add(key, cached);
            }
            catch
            {
                texture.Dispose();
                throw;
            }
        }

        Rect bounds = glyph.Bounds;
        encoder.DrawImage(
            cached.Image,
            new(
                origin.X + bounds.X * scale,
                origin.Y + bounds.Y * scale,
                bounds.Width * scale,
                bounds.Height * scale));
    }

    private int DrawGlyph(
        CommandEncoder encoder,
        FontFace font,
        uint glyphId,
        PathGeometry path,
        Vector2 origin,
        float scale,
        float effectiveSize,
        Brush brush,
        TextRenderingMode mode,
        TextDrawOptions options)
    {
        Matrix3x2 transform = new(scale, 0, 0, scale, origin.X, origin.Y);
        switch (mode)
        {
            case TextRenderingMode.Coverage:
            case TextRenderingMode.SignedDistance:
            case TextRenderingMode.MultiChannelSignedDistance:
                CachedDistanceField cached = GetDistanceField(
                    font,
                    glyphId,
                    path,
                    mode,
                    effectiveSize,
                    options.FillRule,
                    options.DistanceRange);
                encoder.DrawDistanceField(cached.Field, cached.Destination(origin, scale), brush);
                return 0;

            case TextRenderingMode.Polygon:
                if (options.FillRule == FillRule.NonZero
                    && TryGetPolygon(font, glyphId, effectiveSize, out PolygonGeometry? polygon))
                {
                    encoder.DrawGeometry(polygon, transform, brush);
                    return 0;
                }
                encoder.DrawPath(path, transform, brush, options.FillRule);
                return 1;

            case TextRenderingMode.VectorPath:
                encoder.DrawPath(path, transform, brush, options.FillRule);
                return 0;

            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }

    private bool TryDrawColorGlyph(
        CommandEncoder encoder,
        FontFace font,
        ColorGlyph glyph,
        ReadOnlySpan<Color> palette,
        Vector2 origin,
        float scale,
        float effectiveSize,
        Brush foreground,
        TextRenderingMode mode,
        TextDrawOptions options,
        out int fallbackCount)
    {
        const uint ForegroundColorIndex = 0xFFFF;

        fallbackCount = 0;
        if (glyph.Layers.Count == 0)
        {
            return false;
        }
        foreach (ColorGlyphLayer layer in glyph.Layers)
        {
            if (layer.ColorIndex != ForegroundColorIndex && layer.ColorIndex >= palette.Length)
            {
                return false;
            }
        }

        foreach (ColorGlyphLayer layer in glyph.Layers)
        {
            Brush brush = layer.ColorIndex == ForegroundColorIndex
                ? foreground
                : Brush.Solid(palette[checked((int)layer.ColorIndex)]);
            fallbackCount |= DrawGlyph(
                encoder,
                font,
                layer.GlyphId,
                layer.Path,
                origin,
                scale,
                effectiveSize,
                brush,
                mode,
                options);
        }
        fallbackCount = fallbackCount == 0 ? 0 : 1;
        return true;
    }

    private CachedDistanceField GetDistanceField(
        FontFace font,
        uint glyphId,
        PathGeometry path,
        TextRenderingMode mode,
        float effectiveSize,
        FillRule fillRule,
        float distanceRange)
    {
        DistanceFieldEncoding encoding = mode switch
        {
            TextRenderingMode.Coverage => DistanceFieldEncoding.Coverage,
            TextRenderingMode.SignedDistance => DistanceFieldEncoding.SignedDistance,
            TextRenderingMode.MultiChannelSignedDistance => DistanceFieldEncoding.MultiChannelSignedDistance,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
        int rasterEmSize = mode switch
        {
            TextRenderingMode.Coverage => Math.Clamp((int)MathF.Ceiling(effectiveSize), 8, 256),
            TextRenderingMode.SignedDistance => 48,
            TextRenderingMode.MultiChannelSignedDistance => 64,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
        var key = new DistanceFieldKey(
            font,
            glyphId,
            encoding,
            rasterEmSize,
            fillRule,
            distanceRange);
        if (distanceFields.TryGetValue(key, out CachedDistanceField cached))
        {
            return cached;
        }

        float pixelsPerUnit = rasterEmSize / (float)font.UnitsPerEm;
        float requestedPadding = MathF.Ceiling(distanceRange) + 1;
        uint width = checked((uint)Math.Max(
            2,
            MathF.Ceiling(path.Bounds.Width * pixelsPerUnit + requestedPadding * 2)));
        uint height = checked((uint)Math.Max(
            2,
            MathF.Ceiling(path.Bounds.Height * pixelsPerUnit + requestedPadding * 2)));
        float actualPadding = MathF.Min(
            requestedPadding,
            MathF.Min(width, height) * 0.25f);
        float horizontalScale = MathF.Max(width - actualPadding * 2, 1) / MathF.Max(path.Bounds.Width, 0.0001f);
        float verticalScale = MathF.Max(height - actualPadding * 2, 1) / MathF.Max(path.Bounds.Height, 0.0001f);
        DistanceFieldRasterizer rasterizer = GetRasterizer(encoding);
        DistanceField field = rasterizer.Rasterize(
            path,
            width,
            height,
            new()
            {
                FillRule = fillRule,
                Encoding = encoding,
                DistanceRange = distanceRange,
            });
        cached = new(
            field,
            path.Bounds,
            width,
            height,
            actualPadding,
            horizontalScale,
            verticalScale);
        distanceFields.Add(key, cached);
        return cached;
    }

    private bool TryGetPolygon(
        FontFace font,
        uint glyphId,
        float effectiveSize,
        [NotNullWhen(true)] out PolygonGeometry? geometry)
    {
        int tessellationSize = Math.Clamp(
            checked((int)MathF.Ceiling(effectiveSize / 32) * 32),
            32,
            512);
        var key = new PolygonKey(font, glyphId, tessellationSize);
        if (polygons.TryGetValue(key, out geometry))
        {
            return geometry is not null;
        }

        float tolerance = font.UnitsPerEm / (float)tessellationSize * 0.25f;
        geometry = font.TryGetGlyphOutline(glyphId, out GlyphOutline? outline)
            && GlyphPolygonFactory.TryCreate(outline, tolerance, out PolygonGeometry? created)
                ? created
                : null;
        polygons.Add(key, geometry);
        return geometry is not null;
    }

    private DistanceFieldRasterizer GetRasterizer(DistanceFieldEncoding encoding)
    {
        if (encoding == DistanceFieldEncoding.MultiChannelSignedDistance)
        {
            multiChannelAtlas ??= new(backend, atlasWidth, atlasHeight, GpuFormat.Rgba8Unorm);
            return multiChannelRasterizer ??= new(backend, multiChannelAtlas);
        }

        singleChannelAtlas ??= new(backend, atlasWidth, atlasHeight, GpuFormat.R8Unorm);
        return singleChannelRasterizer ??= new(backend, singleChannelAtlas);
    }

    private void VerifyAlive() => ObjectDisposedException.ThrowIf(disposed, this);

    private void VerifyEncoder(CommandEncoder encoder)
    {
        if (!renderer.Owns(encoder))
        {
            throw new ArgumentException("Command encoder belongs to another 2D renderer.", nameof(encoder));
        }
    }

    private readonly record struct DistanceFieldKey(
        FontFace Font,
        uint GlyphId,
        DistanceFieldEncoding Encoding,
        int RasterEmSize,
        FillRule FillRule,
        float DistanceRange);

    private readonly record struct PolygonKey(FontFace Font, uint GlyphId, int TessellationSize);

    private readonly record struct ColorBitmapKey(
        FontFace Font,
        uint GlyphId,
        uint Width,
        uint Height,
        ColorBitmapContentId ContentId);

    private readonly record struct PaintClip(Rect? Bounds, bool IsRecorded);

    private readonly record struct CachedColorBitmap(ImageId Image, ColorBitmapTexture Texture);

    private readonly record struct CachedDistanceField(
        DistanceField Field,
        Rect PathBounds,
        uint Width,
        uint Height,
        float Padding,
        float HorizontalScale,
        float VerticalScale)
    {
        public Rect Destination(Vector2 origin, float glyphScale)
        {
            float paddingX = Padding / HorizontalScale;
            float paddingY = Padding / VerticalScale;
            return new(
                origin.X + (PathBounds.X - paddingX) * glyphScale,
                origin.Y + (PathBounds.Y - paddingY) * glyphScale,
                Width / HorizontalScale * glyphScale,
                Height / VerticalScale * glyphScale);
        }
    }
}
