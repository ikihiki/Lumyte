using System.Numerics;
using System.Runtime.InteropServices;

using Lumyte.Graphics.Text;
using Lumyte.Graphics.TwoD;

using SkiaSharp;

namespace Lumyte.Graphics.RenderGraph.Conformance;

/// <summary>Test-only independent renderer. Curves, strokes and clips go directly to Skia, never through GPU preparation.</summary>
public static class SkiaTwoDReference
{
    public static byte[] Render(Draw2DScene scene, int size = TwoDRenderConsumer.Size)
        => new Renderer(8).RenderColor(scene, size, SKColorType.Rgba8888);

    /// <summary>Returns native-endian half-float premultiplied linear RGBA, preserving HDR through every layer.</summary>
    public static byte[] RenderHalf(Draw2DScene scene, int size = TwoDRenderConsumer.Size)
        => new Renderer(8).RenderColor(scene, size, SKColorType.RgbaF16);

    private sealed class Renderer(int supersampling)
    {
        private static bool Antialias => true;
        public byte[] RenderColor(Draw2DScene scene, int size, SKColorType colorType)
        {
            using var linear = SKColorSpace.CreateSrgbLinear();
            int renderSize = checked(size * supersampling);
            var info = new SKImageInfo(renderSize, renderSize, colorType, SKAlphaType.Premul, linear);
            using var surface = SKSurface.Create(info);
            surface.Canvas.Clear(SKColors.Transparent);
            DrawScene(surface.Canvas, scene, Matrix3x2.CreateScale(scene.DeviceScale * supersampling), linear, scene.DeviceScale * supersampling, renderSize);
            using var bitmap = new SKBitmap(info);
            if (!surface.ReadPixels(info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0))
            { throw new InvalidOperationException("Skia readback failed."); }
            byte[] pixels = new byte[renderSize * renderSize * info.BytesPerPixel];
            Marshal.Copy(bitmap.GetPixels(), pixels, 0, pixels.Length);
            return supersampling == 1 ? pixels : Downsample(pixels, size, colorType);
        }

        private void DrawScene(SKCanvas canvas, Draw2DScene scene, Matrix3x2 parent, SKColorSpace linear, float deviceScale, int size)
        {
            foreach (Draw2DCommand command in scene.Commands)
            {
                canvas.Save();
                try
                {
                    foreach (Draw2DClip clip in command.State.Clips)
                    {
                        canvas.SetMatrix(Matrix(clip.Transform * parent));
                        if (clip.Rectangle is { } rectangle)
                        { canvas.ClipRect(Rectangle(rectangle), SKClipOperation.Intersect, Antialias); }
                        else
                        { using var path = Path(clip.Path!, clip.FillRule); canvas.ClipPath(path, SKClipOperation.Intersect, Antialias); }
                    }
                    Matrix3x2 world = command.State.Transform * parent;
                    canvas.SetMatrix(Matrix(world));
                    switch (command)
                    {
                        case Draw2DShapeCommand shape:
                            using (var paint = Paint(shape.Brush, linear, canvas))
                            {
                                if (shape.Kind == Draw2DShapeKind.Ellipse)
                                { canvas.DrawOval(Rectangle(shape.Bounds), paint); }
                                else if (shape.Kind == Draw2DShapeKind.RoundedRectangle)
                                {
                                    using var round = new SKRoundRect();
                                    var radius = shape.CornerRadius;
                                    float max = Math.Min(shape.Bounds.Width, shape.Bounds.Height) / 2;
                                    round.SetRectRadii(Rectangle(shape.Bounds), [Point(Math.Min(max, radius.TopLeft)), Point(Math.Min(max, radius.TopRight)),
                                    Point(Math.Min(max, radius.BottomRight)), Point(Math.Min(max, radius.BottomLeft))]);
                                    canvas.DrawRoundRect(round, paint);
                                }
                                else
                                { canvas.DrawRect(Rectangle(shape.Bounds), paint); }
                            }
                            break;
                        case Draw2DPathCommand pathCommand:
                            using (var path = Path(pathCommand.Path, pathCommand.FillRule))
                            using (var paint = Paint(pathCommand.Brush, linear, canvas))
                            {
                                if (pathCommand.Stroke is { } stroke)
                                {
                                    paint.Style = SKPaintStyle.Stroke;
                                    paint.StrokeWidth = stroke.Width;
                                    paint.StrokeMiter = stroke.MiterLimit;
                                    paint.StrokeCap = stroke.Cap switch { StrokeCap.Square => SKStrokeCap.Square, StrokeCap.Round => SKStrokeCap.Round, _ => SKStrokeCap.Butt };
                                    paint.StrokeJoin = stroke.Join switch { StrokeJoin.Bevel => SKStrokeJoin.Bevel, StrokeJoin.Round => SKStrokeJoin.Round, _ => SKStrokeJoin.Miter };
                                    if (stroke.Dashes.Count != 0)
                                    { paint.PathEffect = SKPathEffect.CreateDash(stroke.Dashes.ToArray(), stroke.DashOffset); }
                                }
                                canvas.DrawPath(path, paint);
                            }
                            break;
                        case Draw2DGeometryCommand geometry:
                            using (var path = new SKPath())
                            using (var paint = Paint(geometry.Brush, linear, canvas))
                            {
                                for (int index = 0; index < geometry.Geometry.Vertices.Count; index += 3)
                                {
                                    path.MoveTo(Point(geometry.Geometry.Vertices[index]));
                                    path.LineTo(Point(geometry.Geometry.Vertices[index + 1]));
                                    path.LineTo(Point(geometry.Geometry.Vertices[index + 2]));
                                    path.Close();
                                }
                                canvas.DrawPath(path, paint);
                            }
                            break;
                        case Draw2DImageCommand image:
                            DrawImage(canvas, image.Source.Upload!, image.SourceRectangle, image.Destination, image.Tint, image.Sampling, linear);
                            break;
                        case Draw2DSceneCommand nested:
                            DrawScene(canvas, nested.Content, world, linear, deviceScale, size);
                            break;
                        case Draw2DLayerCommand layer:
                            DrawLayer(canvas, layer, world, linear, deviceScale, size);
                            break;
                        case Draw2DTextCommand text:
                            DrawText(canvas, text, world, linear);
                            break;
                        case Draw2DDistanceFieldCommand distance:
                            DrawDistanceField(canvas, distance, linear);
                            break;
                        default:
                            throw new NotSupportedException($"Skia reference does not handle {command.GetType().Name}.");
                    }
                }
                finally { canvas.Restore(); }
            }
        }

        private void DrawLayer(SKCanvas canvas, Draw2DLayerCommand layer, Matrix3x2 world, SKColorSpace linear, float deviceScale, int size)
        {
            // Parent clipping must not trim the source before blur, mask, or shadow is evaluated.
            var info = new SKImageInfo(size, size, SKColorType.RgbaF16, SKAlphaType.Premul, linear);
            using var content = SKSurface.Create(info);
            content.Canvas.Clear(SKColors.Transparent);
            DrawScene(content.Canvas, layer.Content, world, linear, deviceScale, size);
            using var original = content.Snapshot();

            using var processed = SKSurface.Create(info);
            processed.Canvas.Clear(SKColors.Transparent);
            using (var blurred = BlurImage(original, layer.Options.BlurRadius * deviceScale, info))
            { processed.Canvas.DrawImage(blurred, 0, 0); }
            if (layer.Options.Mask is { } mask)
            {
                // Drawing only the mask rectangle with DstIn would leave the surrounding content unchanged.
                using var maskSurface = SKSurface.Create(info);
                maskSurface.Canvas.Clear(SKColors.Transparent);
                maskSurface.Canvas.SetMatrix(Matrix(world));
                var source = mask.Image.Upload ?? throw new NotSupportedException("Skia reference requires a supplied bitmap for logical image masks.");
                DrawImage(maskSurface.Canvas, source, new(0, 0, source.Description.Width, source.Description.Height), mask.Destination, Color.White, default, linear);
                using var image = maskSurface.Snapshot();
                using var maskPaint = new SKPaint { BlendMode = SKBlendMode.DstIn };
                processed.Canvas.DrawImage(image, 0, 0, maskPaint);
            }

            canvas.Save();
            try
            {
                canvas.SetMatrix(Matrix(world));
                if (layer.Options.Bounds is { } bounds)
                { canvas.ClipRect(Rectangle(bounds), SKClipOperation.Intersect, Antialias); }
                canvas.ResetMatrix();
                if (layer.Options.Shadow is { } shadow)
                {
                    Vector2 offset = Vector2.TransformNormal(shadow.Offset, world);
                    using var tinted = MapImage(original, info, 1, shadow.Color);
                    using var blurred = BlurImage(tinted, shadow.BlurRadius * deviceScale, info);
                    using var shadowPaint = new SKPaint();
                    shadowPaint.SetColor(new SKColorF(1, 1, 1, layer.Options.Opacity), linear);
                    canvas.DrawImage(blurred, offset.X, offset.Y, new SKSamplingOptions(SKFilterMode.Linear), shadowPaint);
                }
                using var result = processed.Snapshot();
                using var composite = new SKPaint { BlendMode = Blend(layer.Options.CompositeMode) };
                composite.SetColor(new SKColorF(1, 1, 1, layer.Options.Opacity), linear);
                canvas.DrawImage(result, 0, 0, composite);
            }
            finally { canvas.Restore(); }
        }


        private SKImage BlurImage(SKImage image, float radius, SKImageInfo info)
        {
            using var target = SKSurface.Create(info);
            if (radius == 0)
            { target.Canvas.DrawImage(image, 0, 0); return target.Snapshot(); }
            // Skia's raster blur clips HDR channels even for an F16 destination.
            // Gaussian filtering is linear, so normalize RGB and restore its scale after filtering.
            using var bitmap = new SKBitmap(info);
            if (!image.ReadPixels(info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0))
            { throw new InvalidOperationException("Skia HDR range readback failed."); }
            byte[] bytes = new byte[bitmap.ByteCount];
            Marshal.Copy(bitmap.GetPixels(), bytes, 0, bytes.Length);
            float scale = 1;
            for (int offset = 0; offset < bytes.Length; offset += 8)
            {
                float alpha = (float)BitConverter.ToHalf(bytes, offset + 6);
                if (alpha <= 0)
                { continue; }
                for (int channel = 0; channel < 3; channel++)
                { scale = Math.Max(scale, (float)BitConverter.ToHalf(bytes, offset + channel * 2) / alpha); }
            }
            using var normalized = MapImage(image, info, 1 / scale);
            using var blur = SKImageFilter.CreateBlur(radius / 3, radius / 3, SKShaderTileMode.Decal);
            using var paint = new SKPaint { ImageFilter = blur };
            target.Canvas.DrawImage(normalized, 0, 0, paint);
            using var filtered = target.Snapshot();
            return MapImage(filtered, info, scale);
        }

        private SKImage MapImage(SKImage image, SKImageInfo info, float scale, Color? tint = null)
        {
            using var target = SKSurface.Create(info);
            using var input = image.ToShader(SKShaderTileMode.Decal, SKShaderTileMode.Decal, new SKSamplingOptions(SKFilterMode.Nearest));
            using var effect = SKRuntimeEffect.CreateShader("uniform shader source; uniform float4 factor; uniform float tint; half4 main(float2 p) { half4 c=source.eval(p); return tint>0 ? c.a*factor : c*half4(factor.rgb,1); }", out var errors)
                ?? throw new InvalidOperationException(errors);
            Vector4 factor = tint?.Premultiplied() ?? new(scale, scale, scale, 1);
            var uniforms = new SKRuntimeEffectUniforms(effect) { ["factor"] = new[] { factor.X, factor.Y, factor.Z, factor.W }, ["tint"] = tint is null ? 0f : 1f };
            using var children = new SKRuntimeEffectChildren(effect) { ["source"] = input };
            using var shader = effect.ToShader(uniforms, children);
            using var paint = new SKPaint { Shader = shader, BlendMode = SKBlendMode.Src };
            target.Canvas.DrawPaint(paint);
            return target.Snapshot();
        }

        private void DrawText(SKCanvas canvas, Draw2DTextCommand text, Matrix3x2 world, SKColorSpace linear)
        {
            foreach (var positioned in text.Data.GlyphRuns.SelectMany(run => run.Glyphs))
            {
                var transform = positioned.Transform * Matrix3x2.CreateTranslation(text.Origin) * world;
                canvas.Save();
                canvas.SetMatrix(Matrix(transform));
                try
                {
                    if (positioned.Glyph.ColorPaint is { } colorPaint)
                    { DrawGlyphPaint(canvas, colorPaint, text.Brush, transform, linear); }
                    else if (positioned.Glyph.Outline is { } outline)
                    {
                        using var path = Path(outline.Path, outline.FillRule);
                        using var paint = Paint(text.Brush, linear, canvas);
                        canvas.DrawPath(path, paint);
                    }
                }
                finally { canvas.Restore(); }
            }
        }
        private void DrawGlyphPaint(SKCanvas canvas, GlyphPaintNode node, Brush foreground, Matrix3x2 world, SKColorSpace linear)
        {
            canvas.Save();
            try
            {
                switch (node)
                {
                    case GlyphPaintNode.Outline outline:
                        using (var path = Path(outline.Path, outline.FillRule))
                        { canvas.ClipPath(path, SKClipOperation.Intersect, Antialias); }
                        DrawGlyphPaint(canvas, outline.Paint, foreground, world, linear);
                        break;
                    case GlyphPaintNode.Clip clip:
                        using (var path = Path(clip.Path, clip.FillRule))
                        { canvas.ClipPath(path, SKClipOperation.Intersect, Antialias); }
                        DrawGlyphPaint(canvas, clip.Child, foreground, world, linear);
                        break;
                    case GlyphPaintNode.Solid solid:
                        using (var paint = Paint(Brush.Solid(solid.Color), linear, canvas))
                        { canvas.DrawPaint(paint); }
                        break;
                    case GlyphPaintNode.Foreground inherited:
                        using (var paint = Paint(foreground, linear, canvas))
                        { paint.ColorF = paint.ColorF.WithAlpha(paint.ColorF.Alpha * inherited.Alpha); canvas.DrawPaint(paint); }
                        break;
                    case GlyphPaintNode.Gradient gradient:
                        using (var paint = Paint(gradient.Brush, linear, canvas))
                        { canvas.DrawPaint(paint); }
                        break;
                    case GlyphPaintNode.Bitmap bitmap:
                        DrawImage(canvas, bitmap.Image, bitmap.SourceRectangle, bitmap.Destination, Color.White, default, linear);
                        break;
                    case GlyphPaintNode.Layers layers:
                        foreach (var child in layers.Children)
                        { DrawGlyphPaint(canvas, child, foreground, world, linear); }
                        break;
                    case GlyphPaintNode.Transform transform:
                        canvas.SetMatrix(Matrix(transform.Matrix * world));
                        DrawGlyphPaint(canvas, transform.Child, foreground, transform.Matrix * world, linear);
                        break;
                    case GlyphPaintNode.Composite composite:
                        canvas.SaveLayer();
                        DrawGlyphPaint(canvas, composite.Backdrop, foreground, world, linear);
                        using (var paint = new SKPaint { BlendMode = Blend(composite.Mode) })
                        { canvas.SaveLayer(paint); DrawGlyphPaint(canvas, composite.Source, foreground, world, linear); canvas.Restore(); }
                        canvas.Restore();
                        break;
                }
            }
            finally { canvas.Restore(); }
        }

        private void DrawDistanceField(SKCanvas canvas, Draw2DDistanceFieldCommand command, SKColorSpace linear)
        {
            // An explicitly encoded coverage fixture uses Skia's alpha mask path; SDF fixture images are decoded by their public distance convention.
            var data = command.Data;
            var source = data.Image.Subresources[0];
            int width = (int)data.Image.Description.Width, height = (int)data.Image.Description.Height;
            using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Alpha8, SKAlphaType.Premul));
            byte[] mask = new byte[bitmap.RowBytes * height];
            int bpp = data.Image.Description.Format == GpuFormat.R8Unorm ? 1 : 4;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int offset = y * (int)source.RowStride + x * bpp;
                    float value = source.Data.Span[offset] / 255f;
                    if (data.Kind == DistanceFieldKind.Msdf)
                    {
                        float green = source.Data.Span[offset + 1] / 255f, blue = source.Data.Span[offset + 2] / 255f;
                        value = Math.Max(Math.Min(value, green), Math.Min(Math.Max(value, green), blue));
                    }
                    float coverage = data.Kind == DistanceFieldKind.Coverage ? value : Math.Clamp((value - 0.5f) * data.DistanceRange + 0.5f, 0, 1);
                    mask[y * bitmap.RowBytes + x] = (byte)MathF.Round(coverage * 255);
                }
            }
            Marshal.Copy(mask, 0, bitmap.GetPixels(), mask.Length);
            using var image = SKImage.FromBitmap(bitmap);
            using var paint = Paint(command.Brush, linear, canvas);
            canvas.DrawImage(image, Rectangle(command.Destination), new SKSamplingOptions(SKFilterMode.Linear), paint);
        }

        private SKPaint Paint(Brush brush, SKColorSpace linear, SKCanvas canvas)
        {
            var paint = new SKPaint { IsAntialias = Antialias };
            paint.SetColor(new SKColorF(1, 1, 1, 1), linear);
            if (brush is SolidBrush solid)
            { paint.SetColor(ColorValue(solid.Color), linear); return SnapPaint(paint, canvas); }
            if (brush is GradientBrush gradient)
            {
                if (gradient.Stops.All(stop => stop.Color.Alpha == 1))
                {
                    paint.Shader = GradientShader(gradient, gradient.Stops.Select(stop => ColorValue(stop.Color)).ToArray(), linear);
                    return SnapPaint(paint, canvas);
                }
                // SkiaSharp's gradient overloads interpolate straight-alpha colors. Two opaque Skia
                // gradients retain Skia's geometry/interpolation while this adapter only assembles premultiplied RGBA.
                using var rgb = GradientShader(gradient, gradient.Stops.Select(stop =>
                    new SKColorF(stop.Color.Red * stop.Color.Alpha, stop.Color.Green * stop.Color.Alpha, stop.Color.Blue * stop.Color.Alpha, 1)).ToArray(), linear);
                using var alpha = GradientShader(gradient, gradient.Stops.Select(stop =>
                    new SKColorF(stop.Color.Alpha, stop.Color.Alpha, stop.Color.Alpha, 1)).ToArray(), linear);
                using var effect = SKRuntimeEffect.CreateShader("uniform shader rgb; uniform shader alpha; half4 main(float2 p) { return half4(rgb.eval(p).rgb, alpha.eval(p).r); }", out var errors)
                    ?? throw new InvalidOperationException(errors);
                using var children = new SKRuntimeEffectChildren(effect) { ["rgb"] = rgb, ["alpha"] = alpha };
                paint.Shader = effect.ToShader(new SKRuntimeEffectUniforms(effect), children);
                return SnapPaint(paint, canvas);
            }
            if (brush is ImageBrush image)
            {
                using var bitmap = Image(image.Source.Upload ?? throw new NotSupportedException("Skia reference requires supplied pixels for an image brush."));
                paint.Shader = bitmap.ToShader(Tile(image.Sampling.ExtendX), Tile(image.Sampling.ExtendY),
                    new SKSamplingOptions(image.Sampling.Filter == Draw2DImageFilter.Nearest ? SKFilterMode.Nearest : SKFilterMode.Linear), Matrix(image.Transform));
                return SnapPaint(paint, canvas);
            }
            throw new NotSupportedException($"Skia reference brush {brush.GetType().Name}.");
        }

        private SKPaint SnapPaint(SKPaint paint, SKCanvas canvas)
        {
            if (paint.Shader is { } shader)
            { paint.Shader = SampleAtPixelCenters(shader, canvas); }
            return paint;
        }

        private SKShader SampleAtPixelCenters(SKShader source, SKCanvas canvas)
        {
            SKMatrix current = canvas.TotalMatrix;
            Matrix3x2 forward = new Matrix3x2(current.ScaleX, current.SkewY, current.SkewX, current.ScaleY, current.TransX, current.TransY)
                * Matrix3x2.CreateScale(1f / supersampling);
            if (!Matrix3x2.Invert(forward, out Matrix3x2 inverse))
            { inverse = Matrix3x2.Identity; }
            using var effect = SKRuntimeEffect.CreateShader("""
                uniform shader source;
                uniform float4 forward; uniform float2 offset;
                uniform float4 inverseMap; uniform float2 translation;
                half4 main(float2 p) {
                    float2 pixel = float2(p.x*forward.x+p.y*forward.z, p.x*forward.y+p.y*forward.w)+offset;
                    pixel = floor(pixel)+0.5;
                    float2 local = float2(pixel.x*inverseMap.x+pixel.y*inverseMap.z,pixel.x*inverseMap.y+pixel.y*inverseMap.w)+translation;
                    return source.eval(local);
                }
                """, out var errors) ?? throw new InvalidOperationException(errors);
            var uniforms = new SKRuntimeEffectUniforms(effect)
            {
                ["forward"] = new[] { forward.M11, forward.M12, forward.M21, forward.M22 },
                ["offset"] = new[] { forward.M31, forward.M32 },
                ["inverseMap"] = new[] { inverse.M11, inverse.M12, inverse.M21, inverse.M22 },
                ["translation"] = new[] { inverse.M31, inverse.M32 }
            };
            using var children = new SKRuntimeEffectChildren(effect) { ["source"] = source };
            return effect.ToShader(uniforms, children);
        }

        private SKShader GradientShader(GradientBrush gradient, SKColorF[] colors, SKColorSpace linear)
        {
            var offsets = gradient.Stops.Select(stop => stop.Offset).ToArray();
            SKShaderTileMode tile = gradient.ExtendMode switch { GradientExtendMode.Repeat => SKShaderTileMode.Repeat, GradientExtendMode.Reflect => SKShaderTileMode.Mirror, _ => SKShaderTileMode.Clamp };
            return gradient switch
            {
                LinearGradientBrush line => SKShader.CreateLinearGradient(new(0, 0), new(1, 0), colors, linear, offsets, tile,
                    Matrix(new(line.End.X - line.Start.X, line.End.Y - line.Start.Y, line.Projection.X - line.Start.X, line.Projection.Y - line.Start.Y, line.Start.X, line.Start.Y))),
                RadialGradientBrush radial => SKShader.CreateTwoPointConicalGradient(Point(radial.Center0), radial.Radius0, Point(radial.Center1), radial.Radius1, colors, linear, offsets, tile),
                SweepGradientBrush sweep => SKShader.CreateSweepGradient(Point(sweep.Center), colors, linear, offsets, tile, sweep.StartAngle * 180 / MathF.PI, sweep.EndAngle * 180 / MathF.PI),
                _ => throw new NotSupportedException(),
            };
        }
        private SKShaderTileMode Tile(Draw2DImageExtend extend) => extend switch
        { Draw2DImageExtend.Repeat => SKShaderTileMode.Repeat, Draw2DImageExtend.Mirror => SKShaderTileMode.Mirror, _ => SKShaderTileMode.Clamp };
        private void DrawImage(SKCanvas canvas, GpuImageUploadData data, Rect source, Rect destination, Color tint, Draw2DImageSampling sampling, SKColorSpace linear)
        {
            using var image = Image(data);
            using var paint = new SKPaint { IsAntialias = Antialias };
            paint.SetColor(new SKColorF(1, 1, 1, tint.Alpha), linear);
            if (tint.Red != 1 || tint.Green != 1 || tint.Blue != 1)
            {
                paint.ColorFilter = SKColorFilter.CreateColorMatrix([tint.Red, 0, 0, 0, 0, 0, tint.Green, 0, 0, 0, 0, 0, tint.Blue, 0, 0, 0, 0, 0, 1, 0]);
            }
            float sx = destination.Width / source.Width, sy = destination.Height / source.Height;
            Matrix3x2 imageToLocal = new(sx, 0, 0, sy, destination.X - source.X * sx, destination.Y - source.Y * sy);
            using var sampled = image.ToShader(Tile(sampling.ExtendX), Tile(sampling.ExtendY),
                new SKSamplingOptions(sampling.Filter == Draw2DImageFilter.Nearest ? SKFilterMode.Nearest : SKFilterMode.Linear), Matrix(imageToLocal));
            paint.Shader = SampleAtPixelCenters(sampled, canvas);
            canvas.DrawRect(Rectangle(destination), paint);
        }
        private SKImage Image(GpuImageUploadData data)
        {
            using var space = data.Encoding == GpuImageColorEncoding.Srgb ? SKColorSpace.CreateSrgb() : SKColorSpace.CreateSrgbLinear();
            var alpha = data.AlphaMode switch { GpuImageAlphaMode.Opaque => SKAlphaType.Opaque, GpuImageAlphaMode.Straight => SKAlphaType.Unpremul, _ => SKAlphaType.Premul };
            SKColorType color = data.Description.Format switch { GpuFormat.Bgra8Unorm or GpuFormat.Bgra8UnormSrgb => SKColorType.Bgra8888, GpuFormat.Rgba16Float => SKColorType.RgbaF16, _ => SKColorType.Rgba8888 };
            using var bitmap = new SKBitmap(new SKImageInfo((int)data.Description.Width, (int)data.Description.Height, color, alpha, space));
            var source = data.Subresources[0];
            for (int y = 0; y < bitmap.Height; y++)
            { Marshal.Copy(source.Data.Slice(y * (int)source.RowStride, bitmap.Width * bitmap.BytesPerPixel).ToArray(), 0, bitmap.GetPixels() + y * bitmap.RowBytes, bitmap.Width * bitmap.BytesPerPixel); }
            using var decoded = SKImage.FromBitmap(bitmap);
            using var linear = SKColorSpace.CreateSrgbLinear();
            using var converted = SKSurface.Create(new SKImageInfo(bitmap.Width, bitmap.Height, SKColorType.RgbaF16, SKAlphaType.Premul, linear));
            using var copy = new SKPaint { BlendMode = SKBlendMode.Src };
            // Convert source color encoding and alpha once at 1:1, then let Skia filter the linear premultiplied image.
            converted.Canvas.DrawImage(decoded, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest), copy);
            return converted.Snapshot();
        }
        private SKPath Path(PathGeometry geometry, FillRule fillRule)
        {
            var path = new SKPath { FillType = fillRule == FillRule.EvenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding };
            foreach (var segment in geometry.Segments)
            {
                switch (segment.Kind)
                {
                    case PathSegmentKind.Move:
                        path.MoveTo(Point(segment.Point));
                        break;
                    case PathSegmentKind.Line:
                        path.LineTo(Point(segment.Point));
                        break;
                    case PathSegmentKind.Quadratic:
                        path.QuadTo(Point(segment.Control0), Point(segment.Point));
                        break;
                    case PathSegmentKind.Cubic:
                        path.CubicTo(Point(segment.Control0), Point(segment.Control1), Point(segment.Point));
                        break;
                    case PathSegmentKind.Close:
                        path.Close();
                        break;
                }
            }
            return path;
        }
        private SKBlendMode Blend(CompositeMode mode) => mode switch
        {
            CompositeMode.Source => SKBlendMode.Src,
            CompositeMode.Destination => SKBlendMode.Dst,
            CompositeMode.SourceOver => SKBlendMode.SrcOver,
            CompositeMode.DestinationOver => SKBlendMode.DstOver,
            CompositeMode.SourceIn => SKBlendMode.SrcIn,
            CompositeMode.DestinationIn => SKBlendMode.DstIn,
            CompositeMode.SourceOut => SKBlendMode.SrcOut,
            CompositeMode.DestinationOut => SKBlendMode.DstOut,
            CompositeMode.SourceAtop => SKBlendMode.SrcATop,
            CompositeMode.DestinationAtop => SKBlendMode.DstATop,
            CompositeMode.HslHue => SKBlendMode.Hue,
            CompositeMode.HslSaturation => SKBlendMode.Saturation,
            CompositeMode.HslColor => SKBlendMode.Color,
            CompositeMode.HslLuminosity => SKBlendMode.Luminosity,
            _ => Enum.Parse<SKBlendMode>(mode.ToString()),
        };
        private SKPoint Point(Vector2 point) => new(point.X, point.Y);
        private SKPoint Point(float value) => new(value, value);
        private SKRect Rectangle(Rect value) => new(value.X, value.Y, value.Right, value.Bottom);
        private SKColorF ColorValue(Color value) => new(value.Red, value.Green, value.Blue, value.Alpha);
        private SKMatrix Matrix(Matrix3x2 value) => new(value.M11, value.M21, value.M31, value.M12, value.M22, value.M32, 0, 0, 1);
        private byte[] Downsample(byte[] source, int size, SKColorType format)
        {
            bool half = format == SKColorType.RgbaF16;
            int elementSize = half ? 2 : 1, factor = supersampling * supersampling;
            var target = new byte[size * size * 4 * elementSize];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    for (int channel = 0; channel < 4; channel++)
                    {
                        float sum = 0;
                        for (int sy = 0; sy < supersampling; sy++)
                        {
                            for (int sx = 0; sx < supersampling; sx++)
                            {
                                int offset = (((y * supersampling + sy) * size * supersampling + x * supersampling + sx) * 4 + channel) * elementSize;
                                sum += half ? (float)BitConverter.ToHalf(source, offset) : source[offset] / 255f;
                            }
                        }
                        int destination = ((y * size + x) * 4 + channel) * elementSize;
                        float value = sum / factor;
                        if (half)
                        { BitConverter.TryWriteBytes(target.AsSpan(destination, 2), (Half)value); }
                        else
                        { target[destination] = (byte)Math.Clamp(MathF.Round(value * 255), 0, 255); }
                    }
                }
            }
            return target;
        }
    }

}
