using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Drawing2D;
using SixLabors.Fonts;
using SkiaSharp;

namespace Lumyte.Graphics.Text;

/// <summary>A registered font face whose COLR tables and outlines can be recorded as vector 2D paint graphs.</summary>
public sealed class ColorFont2D : IDisposable
{
    private const int MaximumDimension = 4096;
    private readonly SKTypeface _typeface;
    private readonly SKTypeface _outlineTypeface;
    private readonly HashSet<ushort> _colorGlyphs;
    private readonly string _fontName;
    private readonly FontStyle _style;
    private bool _disposed;

    private ColorFont2D(SKTypeface typeface, SKTypeface outlineTypeface, FontFamily family, HashSet<ushort> colorGlyphs)
    {
        _typeface = typeface;
        _outlineTypeface = outlineTypeface;
        Family = family;
        _colorGlyphs = colorGlyphs;
        FontMetrics metrics = family.CreateFont(12).FontMetrics;
        _fontName = metrics.Description.FontNameInvariantCulture?.ToUpper(CultureInfo.InvariantCulture) ?? string.Empty;
        _style = metrics.Description.Style;
    }

    /// <summary>Gets the same font face for shaping with TextOptions and explicit fallback families.</summary>
    public FontFamily Family { get; }

    /// <summary>Loads one font face from the current stream position without taking ownership of the stream.</summary>
    /// <param name="stream">The application-supplied TrueType or OpenType font stream.</param>
    /// <returns>A native font owner, needed only while generating CPU layout snapshots.</returns>
    public static ColorFont2D Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        byte[] bytes = buffer.ToArray();
        using var data = SKData.CreateCopy(bytes);
        SKTypeface typeface = SKTypeface.FromData(data) ?? throw new InvalidDataException("Skia rejected the supplied font face.");
        try
        {
            HashSet<ushort> colorGlyphs = ReadColorGlyphs(typeface);
            var collection = new FontCollection();
            using var shapingStream = new MemoryStream(bytes, writable: false);
            FontFamily family = collection.Add(shapingStream);
            SKTypeface outline = OutlineTypeface2D.Create(typeface);
            try
            {
                return new ColorFont2D(typeface, outline, family, colorGlyphs);
            }
            catch
            {
                outline.Dispose();
                throw;
            }
        }
        catch
        {
            typeface.Dispose();
            throw;
        }
    }

    /// <summary>Returns whether this glyph has a COLR v0 layer list or COLR v1 paint graph.</summary>
    /// <param name="glyphId">The shaped glyph identifier in this registered face.</param>
    /// <returns>True for a COLR base glyph.</returns>
    public bool HasColorGlyph(ushort glyphId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _colorGlyphs.Contains(glyphId);
    }

    /// <summary>Renders one shaped COLR glyph relative to baseline (0,0), resolving paint graphs and blends in Skia.</summary>
    /// <param name="glyphId">The shaped glyph identifier in this registered face.</param>
    /// <param name="fontSize">The finite positive logical em size.</param>
    /// <param name="foreground">The opaque current foreground color used by palette index 0xFFFF.</param>
    /// <param name="pixelsPerUnit">The finite positive raster resolution per logical unit.</param>
    /// <returns>A CPU bitmap using the default font palette and default variation instance.</returns>
    public ColorGlyphBitmap2D RenderGlyph(ushort glyphId, float fontSize, Color2D foreground, float pixelsPerUnit = 2)
        => Render(glyphId, fontSize, 1, foreground, pixelsPerUnit, rotated: false);

    /// <summary>Converts a shaped COLR glyph into a bounded vector paint graph for the 2D backend.</summary>
    /// <param name="glyphId">The shaped glyph identifier in this registered face.</param>
    /// <param name="fontSize">The finite positive logical em size.</param>
    /// <param name="foreground">The current-color paint in logical baseline coordinates; null selects black.</param>
    /// <param name="paletteIndex">The zero-based CPAL palette to select.</param>
    /// <returns>An independent vector paint snapshot at the font's default variation coordinates.</returns>
    public ColorGlyphPaint2D RenderPaintGlyph(ushort glyphId, float fontSize, PaintGraph2D? foreground = null, int paletteIndex = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidatePositive(fontSize, nameof(fontSize));
        if (!_colorGlyphs.Contains(glyphId))
        {
            throw new ArgumentException("The requested glyph does not contain COLR color data.", nameof(glyphId));
        }

        float scale = fontSize / _typeface.UnitsPerEm;
        return ReadPaint(glyphId, Matrix3x2.CreateScale(scale, -scale), foreground ?? PaintGraph2D.Solid(new Color2D(0, 0, 0, 1)), paletteIndex);
    }

    /// <summary>Releases the native font face; previously generated vector and bitmap snapshots remain valid.</summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _outlineTypeface.Dispose();
            _typeface.Dispose();
        }
    }

    internal bool Matches(in GlyphRendererParameters parameters)
        => parameters.FontStyle == _style && string.Equals(parameters.Font, _fontName, StringComparison.OrdinalIgnoreCase);

    internal ColorGlyphPaint2D PaintGlyph(in FontRectangle bounds, in GlyphRendererParameters parameters, PaintGraph2D foreground, int paletteIndex = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        float fontSize = parameters.PointSize * parameters.Dpi / 72;
        float scaleX = 1;
        float scaleY = 1;
        FontMetrics metrics = Family.CreateFont(parameters.PointSize, parameters.FontStyle).FontMetrics;
        if ((parameters.TextRun.TextAttributes & TextAttributes.Subscript) != 0)
        {
            scaleX = (float)metrics.SubscriptXSize / metrics.UnitsPerEm;
            scaleY = (float)metrics.SubscriptYSize / metrics.UnitsPerEm;
        }
        else if ((parameters.TextRun.TextAttributes & TextAttributes.Superscript) != 0)
        {
            scaleX = (float)metrics.SuperscriptXSize / metrics.UnitsPerEm;
            scaleY = (float)metrics.SuperscriptYSize / metrics.UnitsPerEm;
        }

        if (scaleX == 0 || scaleY == 0)
        {
            return new ColorGlyphPaint2D(PaintGraph2D.Solid(default), default);
        }

        if (scaleX < 0 || scaleY < 0)
        {
            throw new NotSupportedException("The font contains negative subscript or superscript size metrics.");
        }

        using var font = new SKFont(_outlineTypeface, fontSize * scaleY, scaleX / scaleY) { Hinting = SKFontHinting.None, Subpixel = true, LinearMetrics = true };
        using SKPath? path = font.GetGlyphPath(parameters.GlyphId);
        SKRect reference = path?.Bounds ?? SKRect.Empty;
        Matrix3x2 rotation = Matrix3x2.Identity;
        if (parameters.LayoutMode == GlyphLayoutMode.VerticalRotated)
        {
            reference = RotateBounds(reference);
            rotation = Matrix3x2.CreateRotation(MathF.PI / 2);
        }

        float emScale = fontSize / _typeface.UnitsPerEm;
        Matrix3x2 transform = Matrix3x2.CreateScale(emScale * scaleX, -emScale * scaleY) * rotation * Matrix3x2.CreateTranslation(bounds.X - reference.Left, bounds.Y - reference.Top);
        return ReadPaint(parameters.GlyphId, transform, foreground, paletteIndex);
    }

    internal ColorGlyphBitmap2D Rasterize(in FontRectangle bounds, in GlyphRendererParameters parameters, Color2D foreground, float pixelsPerUnit)
    {
        float fontSize = parameters.PointSize * parameters.Dpi / 72;
        float xScale = 1;
        if (parameters.TextRun.TextAttributes != TextAttributes.None)
        {
            FontMetrics metrics = Family.CreateFont(parameters.PointSize, parameters.FontStyle).FontMetrics;
            float scaleX = 1;
            float scaleY = 1;
            if ((parameters.TextRun.TextAttributes & TextAttributes.Subscript) != 0)
            {
                scaleX = (float)metrics.SubscriptXSize / metrics.UnitsPerEm;
                scaleY = (float)metrics.SubscriptYSize / metrics.UnitsPerEm;
            }
            else if ((parameters.TextRun.TextAttributes & TextAttributes.Superscript) != 0)
            {
                scaleX = (float)metrics.SuperscriptXSize / metrics.UnitsPerEm;
                scaleY = (float)metrics.SuperscriptYSize / metrics.UnitsPerEm;
            }

            if (scaleX == 0 || scaleY == 0)
            {
                return new ColorGlyphBitmap2D(1, 1, new Rect2D(bounds.X, bounds.Y, 1 / pixelsPerUnit, 1 / pixelsPerUnit), new byte[4]);
            }

            if (scaleX < 0 || scaleY < 0)
            {
                throw new NotSupportedException("The font contains negative subscript or superscript size metrics.");
            }

            fontSize *= scaleY;
            xScale = scaleX / scaleY;
        }

        bool rotated = parameters.LayoutMode == GlyphLayoutMode.VerticalRotated;
        using var font = new SKFont(_outlineTypeface, fontSize, xScale) { Hinting = SKFontHinting.None, Subpixel = true, LinearMetrics = true };
        using SKPath? path = font.GetGlyphPath(parameters.GlyphId);
        SKRect reference = path?.Bounds ?? SKRect.Empty;
        if (rotated)
        {
            reference = RotateBounds(reference);
        }

        ColorGlyphBitmap2D bitmap = Render(parameters.GlyphId, fontSize, xScale, foreground, pixelsPerUnit, rotated);
        return bitmap.Translate(bounds.X - reference.Left, bounds.Y - reference.Top);
    }

    private static SKRect RotateBounds(SKRect bounds) => new(-bounds.Bottom, bounds.Left, -bounds.Top, bounds.Right);

    private static HashSet<ushort> ReadColorGlyphs(SKTypeface typeface)
    {
        var result = new HashSet<ushort>();
        if (!typeface.TryGetTableData(0x434F4C52, out byte[]? table))
        {
            return result;
        }

        if (table.Length < 14)
        {
            throw new InvalidDataException("The COLR table is truncated.");
        }

        ushort version = BinaryPrimitives.ReadUInt16BigEndian(table);
        if (version > 1)
        {
            throw new NotSupportedException("The font uses an unsupported COLR table version.");
        }

        int baseCount = BinaryPrimitives.ReadUInt16BigEndian(table.AsSpan(2));
        uint baseOffset = BinaryPrimitives.ReadUInt32BigEndian(table.AsSpan(4));
        if (baseCount > 0)
        {
            if (baseOffset > table.Length || baseCount > (table.Length - (long)baseOffset) / 6)
            {
                throw new InvalidDataException("The COLR v0 base-glyph records are truncated.");
            }

            for (int i = 0; i < baseCount; i++)
            {
                result.Add(BinaryPrimitives.ReadUInt16BigEndian(table.AsSpan(checked((int)baseOffset + (i * 6)))));
            }
        }

        if (version == 0)
        {
            return result;
        }

        if (table.Length < 34)
        {
            throw new InvalidDataException("The COLR v1 table is truncated.");
        }

        uint offset = BinaryPrimitives.ReadUInt32BigEndian(table.AsSpan(14));
        if (offset == 0)
        {
            return result;
        }

        if (offset > table.Length - 4)
        {
            throw new InvalidDataException("The COLR v1 base-glyph list lies outside its table.");
        }

        int start = checked((int)offset);
        uint count = BinaryPrimitives.ReadUInt32BigEndian(table.AsSpan(start));
        if (count > (table.Length - start - 4) / 6)
        {
            throw new InvalidDataException("The COLR v1 base-glyph records are truncated.");
        }

        for (uint i = 0; i < count; i++)
        {
            result.Add(BinaryPrimitives.ReadUInt16BigEndian(table.AsSpan(start + 4 + checked((int)i * 6))));
        }

        return result;
    }

    private static void ValidatePositive(float value, string name)
    {
        if (!float.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }

    private static void ValidateColor(Color2D color)
    {
        if (!float.IsFinite(color.R) || !float.IsFinite(color.G) || !float.IsFinite(color.B) || !float.IsFinite(color.A) || color.R < 0 || color.R > 1 || color.G < 0 || color.G > 1 || color.B < 0 || color.B > 1 || color.A < 0 || color.A > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(color));
        }
    }

    private ColorGlyphPaint2D ReadPaint(ushort glyphId, Matrix3x2 transform, PaintGraph2D foreground, int paletteIndex)
    {
        try
        {
            using var reader = new ColrPaintReader2D(_typeface, _outlineTypeface, paletteIndex, foreground, transform);
            return reader.Read(glyphId, transform);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException("The COLR graph contains an overflowing record or variation index.", exception);
        }
    }

    private ColorGlyphBitmap2D Render(ushort glyphId, float fontSize, float scaleX, Color2D foreground, float pixelsPerUnit, bool rotated)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidatePositive(fontSize, nameof(fontSize));
        ValidatePositive(pixelsPerUnit, nameof(pixelsPerUnit));
        ValidateColor(foreground);
        if (foreground.A != 1)
        {
            throw new NotSupportedException("Skia applies foreground alpha both inside the COLR graph and to its result. Use an opaque current color and apply group opacity after glyph rendering.");
        }

        if (!_colorGlyphs.Contains(glyphId))
        {
            throw new ArgumentException("The requested glyph does not contain COLR color data.", nameof(glyphId));
        }

        using var font = new SKFont(_typeface, fontSize, scaleX) { Hinting = SKFontHinting.None, Subpixel = true, LinearMetrics = true, Edging = SKFontEdging.Antialias };
        using var paint = new SKPaint { IsAntialias = true, Color = new SKColor((byte)MathF.Round(foreground.R * 255), (byte)MathF.Round(foreground.G * 255), (byte)MathF.Round(foreground.B * 255), (byte)MathF.Round(foreground.A * 255)) };
        font.GetGlyphWidths([glyphId], out SKRect[] rectangles, paint);
        SKRect bounds = rotated ? RotateBounds(rectangles[0]) : rectangles[0];
        int left = checked((int)MathF.Floor(bounds.Left * pixelsPerUnit) - 1);
        int top = checked((int)MathF.Floor(bounds.Top * pixelsPerUnit) - 1);
        int right = checked((int)MathF.Ceiling(bounds.Right * pixelsPerUnit) + 1);
        int bottom = checked((int)MathF.Ceiling(bounds.Bottom * pixelsPerUnit) + 1);
        int width = checked(right - left);
        int height = checked(bottom - top);
        if (width <= 0 || height <= 0 || width > MaximumDimension || height > MaximumDimension)
        {
            throw new NotSupportedException("The color glyph exceeds the 4096-pixel raster dimension budget.");
        }

        using var space = SKColorSpace.CreateSrgb();
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, space);
        using SKSurface surface = SKSurface.Create(info) ?? throw new InvalidOperationException("Skia could not create a color glyph raster surface.");
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Translate(-left, -top);
        canvas.Scale(pixelsPerUnit);
        if (rotated)
        {
            canvas.RotateDegrees(90);
        }

        using var builder = new SKTextBlobBuilder();
        SKPositionedRunBuffer run = builder.AllocatePositionedRun(font, 1);
        run.SetGlyphs([glyphId]);
        run.SetPositions([SKPoint.Empty]);
        using SKTextBlob blob = builder.Build() ?? throw new InvalidOperationException("Skia could not create a shaped glyph blob.");
        canvas.DrawText(blob, 0, 0, paint);
        using var output = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul, space));
        if (!surface.ReadPixels(output.Info, output.GetPixels(), output.RowBytes, 0, 0))
        {
            throw new InvalidOperationException("Skia could not read the color glyph raster.");
        }

        byte[] pixels = new byte[checked(width * height * 4)];
        Marshal.Copy(output.GetPixels(), pixels, 0, pixels.Length);
        return new ColorGlyphBitmap2D(width, height, new Rect2D(left / pixelsPerUnit, top / pixelsPerUnit, width / pixelsPerUnit, height / pixelsPerUnit), pixels);
    }
}
