using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using SkiaSharp;

namespace Lumyte.Graphics.Text;

internal sealed class ColrPaintReader2D : IDisposable
{
    private const int MaximumPaints = 4096;
    private const int MaximumDepth = 96;
    private readonly ColrTable2D _table;
    private readonly ColrTable2D _palette;
    private readonly int _paletteStart;
    private readonly int _paletteEntries;
    private readonly SKFont _font;
    private readonly Dictionary<ushort, int> _basePaints = [];
    private readonly Dictionary<ushort, (int First, int Count)> _legacyLayers = [];
    private readonly Dictionary<ushort, (Path2D Path, Rect2D Bounds)> _outlines = [];
    private readonly HashSet<int> _activePaints = [];
    private readonly HashSet<ushort> _activeGlyphs = [];
    private readonly ColrDefaultVariations2D _variations;
    private readonly PaintGraph2D _foreground;
    private readonly int _layerList;
    private readonly int _clipList;
    private readonly int _legacyLayerList;
    private readonly int _legacyLayerCount;
    private readonly int _glyphCount;
    private Matrix3x2 _coordinate = Matrix3x2.Identity;
    private int _paintCount;

    public ColrPaintReader2D(SKTypeface typeface, SKTypeface outlineTypeface, int paletteIndex, PaintGraph2D foreground, Matrix3x2 transform)
    {
        if (!typeface.TryGetTableData(0x434f4c52, out byte[]? colr) || !typeface.TryGetTableData(0x4350414c, out byte[]? cpal))
        {
            throw new InvalidDataException("COLR and CPAL tables are required for a color glyph.");
        }

        _table = new ColrTable2D(colr);
        _palette = new ColrTable2D(cpal);
        _paletteEntries = _palette.U16(2);
        int palettes = _palette.U16(4);
        if (paletteIndex < 0 || paletteIndex >= palettes)
        {
            throw new ArgumentOutOfRangeException(nameof(paletteIndex));
        }

        int firstColor = _palette.U16(12 + (paletteIndex * 2));
        int colorCount = _palette.U16(6);
        if (firstColor > colorCount - _paletteEntries)
        {
            throw new InvalidDataException("The CPAL palette lies outside its color records.");
        }

        _paletteStart = checked(_palette.Offset32(0, 8) + (firstColor * 4));
        _palette.Check(_paletteStart, _paletteEntries * 4);
        if (!Matrix3x2.Invert(transform, out Matrix3x2 inverse))
        {
            throw new ArgumentException("The glyph transform must be invertible.", nameof(transform));
        }

        _foreground = PaintGraph2D.Transform(inverse, foreground);
        int version = _table.U16(0);
        if (version > 1)
        {
            throw new NotSupportedException("Only COLR v0 and v1 are supported.");
        }

        int legacyBases = _table.U16(2);
        int legacyBaseList = _table.Offset32(0, 4, nullable: legacyBases == 0);
        _legacyLayerCount = _table.U16(12);
        _legacyLayerList = _table.Offset32(0, 8, nullable: _legacyLayerCount == 0);
        _table.Check(legacyBaseList, checked(legacyBases * 6));
        _table.Check(_legacyLayerList, checked(_legacyLayerCount * 4));
        for (int i = 0; i < legacyBases; i++)
        {
            int record = legacyBaseList + (i * 6);
            if (!_legacyLayers.TryAdd(_table.U16(record), (_table.U16(record + 2), _table.U16(record + 4))))
            {
                throw new InvalidDataException("The COLR v0 base-glyph list contains duplicate glyph identifiers.");
            }
        }

        int map = 0;
        int store = 0;
        if (version == 1)
        {
            _table.Check(0, 34);
            int bases = _table.Offset32(0, 14, nullable: true);
            _layerList = _table.Offset32(0, 18, nullable: true);
            _clipList = _table.Offset32(0, 22, nullable: true);
            map = _table.Offset32(0, 26, nullable: true);
            store = _table.Offset32(0, 30, nullable: true);
            if (bases != 0)
            {
                uint count = _table.U32(bases);
                if (count > (_table.Length - bases - 4) / 6)
                {
                    throw new InvalidDataException("The COLR base-glyph paint records are truncated.");
                }

                for (int i = 0; i < count; i++)
                {
                    int record = bases + 4 + (i * 6);
                    if (!_basePaints.TryAdd(_table.U16(record), _table.Offset32(bases, record + 2)))
                    {
                        throw new InvalidDataException("The COLR v1 base-glyph list contains duplicate glyph identifiers.");
                    }
                }
            }
        }

        _variations = new ColrDefaultVariations2D(_table, map, store);
        _glyphCount = typeface.GlyphCount;
        _font = new SKFont(outlineTypeface, typeface.UnitsPerEm) { Hinting = SKFontHinting.None, Subpixel = true, LinearMetrics = true };
    }

    public ColorGlyphPaint2D Read(ushort glyphId, Matrix3x2 transform)
    {
        Node node = Glyph(glyphId, 0);
        if (node.Bounds is null)
        {
            throw new NotSupportedException("An unbounded COLR glyph requires a finite ClipList box or bounded PaintGlyph graph.");
        }

        return new ColorGlyphPaint2D(PaintGraph2D.Transform(transform, node.Paint), TransformBounds(node.Bounds.Value, transform));
    }

    public void Dispose() => _font.Dispose();

    private static Rect2D TransformBounds(Rect2D bounds, Matrix3x2 transform)
    {
        if (bounds.Width == 0 || bounds.Height == 0)
        {
            return default;
        }

        var a = Vector2.Transform(new(bounds.X, bounds.Y), transform);
        var b = Vector2.Transform(new(bounds.X + bounds.Width, bounds.Y), transform);
        var c = Vector2.Transform(new(bounds.X, bounds.Y + bounds.Height), transform);
        var d = Vector2.Transform(new(bounds.X + bounds.Width, bounds.Y + bounds.Height), transform);
        var min = Vector2.Min(Vector2.Min(a, b), Vector2.Min(c, d));
        var max = Vector2.Max(Vector2.Max(a, b), Vector2.Max(c, d));
        return new Rect2D(min.X, min.Y, max.X - min.X, max.Y - min.Y);
    }

    private static Rect2D? Union(Rect2D? a, Rect2D? b)
    {
        if (a is null || b is null)
        {
            return null;
        }

        if (a.Value.Width == 0 || a.Value.Height == 0)
        {
            return b;
        }

        if (b.Value.Width == 0 || b.Value.Height == 0)
        {
            return a;
        }

        float x = MathF.Min(a.Value.X, b.Value.X);
        float y = MathF.Min(a.Value.Y, b.Value.Y);
        return new Rect2D(x, y, MathF.Max(a.Value.X + a.Value.Width, b.Value.X + b.Value.Width) - x, MathF.Max(a.Value.Y + a.Value.Height, b.Value.Y + b.Value.Height) - y);
    }

    private static Rect2D Intersect(Rect2D? a, Rect2D b)
    {
        if (a is null)
        {
            return b;
        }

        float x = MathF.Max(a.Value.X, b.X);
        float y = MathF.Max(a.Value.Y, b.Y);
        return new Rect2D(x, y, MathF.Max(0, MathF.Min(a.Value.X + a.Value.Width, b.X + b.Width) - x), MathF.Max(0, MathF.Min(a.Value.Y + a.Value.Height, b.Y + b.Height) - y));
    }

    private static Path2D Rectangle(Rect2D bounds)
    {
        var path = new PathBuilder2D();
        path.MoveTo(new(bounds.X, bounds.Y));
        path.LineTo(new(bounds.X + bounds.Width, bounds.Y));
        path.LineTo(new(bounds.X + bounds.Width, bounds.Y + bounds.Height));
        path.LineTo(new(bounds.X, bounds.Y + bounds.Height));
        path.Close();
        return path.Build();
    }

    private static Node Composite(Node source, Node backdrop, int mode)
    {
        Rect2D? bounds = mode switch
        {
            0 => default(Rect2D),
            1 or 7 => source.Bounds,
            2 or 8 => backdrop.Bounds,
            5 or 6 => source.Bounds is null ? backdrop.Bounds : backdrop.Bounds is null ? source.Bounds : Intersect(source.Bounds, backdrop.Bounds.Value),
            9 => backdrop.Bounds,
            10 => source.Bounds,
            _ => Union(source.Bounds, backdrop.Bounds),
        };
        return new Node(PaintGraph2D.Composite(source.Paint, backdrop.Paint, (CompositeMode2D)mode), bounds);
    }

    private static Node Empty() => new(PaintGraph2D.Solid(default), default(Rect2D));

    private Node Glyph(ushort glyphId, int depth)
    {
        if (depth > MaximumDepth || !_activeGlyphs.Add(glyphId))
        {
            throw new InvalidDataException("The COLR glyph graph is cyclic or exceeds its depth budget.");
        }

        try
        {
            if (_basePaints.TryGetValue(glyphId, out int paint))
            {
                return ApplyClip(glyphId, Paint(paint, depth + 1));
            }

            if (_legacyLayers.TryGetValue(glyphId, out (int First, int Count) layers))
            {
                if (layers.First > _legacyLayerCount - layers.Count)
                {
                    throw new InvalidDataException("A COLR v0 layer range is invalid.");
                }

                Node result = Empty();
                for (int i = 0; i < layers.Count; i++)
                {
                    int record = _legacyLayerList + ((layers.First + i) * 4);
                    (Path2D path, Rect2D bounds) = Outline(_table.U16(record));
                    var layer = new Node(PaintGraph2D.Clip(path, ColorPaint(_table.U16(record + 2), 1)), bounds);
                    result = i == 0 ? layer : Composite(layer, result, 3);
                }

                return ApplyClip(glyphId, result);
            }

            throw new InvalidDataException("PaintColrGlyph references a glyph without COLR paint data.");
        }
        finally
        {
            _activeGlyphs.Remove(glyphId);
        }
    }

    private Node Paint(int offset, int depth)
    {
        if (depth > MaximumDepth || ++_paintCount > MaximumPaints || !_activePaints.Add(offset))
        {
            throw new InvalidDataException("The COLR paint graph is cyclic or exceeds its traversal budget.");
        }

        try
        {
            int format = _table.U8(offset);
            if (format is < 1 or > 32)
            {
                throw new InvalidDataException("The COLR paint format is unknown.");
            }

            if (format == 1)
            {
                if (_layerList == 0)
                {
                    throw new InvalidDataException("PaintColrLayers requires a LayerList.");
                }

                uint first = _table.U32(offset + 2);
                int count = _table.U8(offset + 1);
                uint total = _table.U32(_layerList);
                if (first > total || count > total - first || total > (_table.Length - _layerList - 4) / 4)
                {
                    throw new InvalidDataException("The COLR layer list range is invalid.");
                }

                Node result = Empty();
                for (int i = 0; i < count; i++)
                {
                    int child = _table.Offset32(_layerList, checked(_layerList + 4 + (((int)first + i) * 4)));
                    Node layer = Paint(child, depth + 1);
                    result = i == 0 ? layer : Composite(layer, result, 3);
                }

                return result;
            }

            if (format is 2 or 3)
            {
                float alpha = _table.F2(offset + 3) + (format == 3 ? Delta(_table.U32(offset + 5), 0) / 16384 : 0);
                return new Node(ColorPaint(_table.U16(offset + 1), Math.Clamp(alpha, 0, 1)), null);
            }

            if (format is >= 4 and <= 9)
            {
                return Gradient(offset, format);
            }

            if (format == 10)
            {
                Node child = Paint(_table.Offset24(offset, offset + 1), depth + 1);
                (Path2D path, Rect2D bounds) = Outline(_table.U16(offset + 4));
                return new Node(PaintGraph2D.Clip(path, child.Paint), Intersect(child.Bounds, bounds));
            }

            if (format == 11)
            {
                ushort glyph = _table.U16(offset + 1);
                if (!_basePaints.ContainsKey(glyph))
                {
                    throw new InvalidDataException("PaintColrGlyph must reference a COLR v1 base-glyph paint record.");
                }

                return Glyph(glyph, depth + 1);
            }

            if (format == 32)
            {
                int mode = _table.U8(offset + 4);
                if (mode > 27)
                {
                    throw new InvalidDataException("The COLR composite mode is invalid.");
                }

                return Composite(Paint(_table.Offset24(offset, offset + 1), depth + 1), Paint(_table.Offset24(offset, offset + 5), depth + 1), mode);
            }

            Matrix3x2 matrix = Transform(offset, format);
            if (!Matrix3x2.Invert(matrix, out _))
            {
                return Empty();
            }

            Matrix3x2 previous = _coordinate;
            _coordinate = matrix * previous;
            Node transformed;
            try
            {
                transformed = Paint(_table.Offset24(offset, offset + 1), depth + 1);
            }
            finally
            {
                _coordinate = previous;
            }

            return new Node(PaintGraph2D.Transform(matrix, transformed.Paint), transformed.Bounds is Rect2D rectangle ? TransformBounds(rectangle, matrix) : null);
        }
        finally
        {
            _activePaints.Remove(offset);
        }
    }

    private Node Gradient(int offset, int format)
    {
        bool variable = (format & 1) != 0;
        int count = format <= 7 ? 6 : 4;
        uint variation = variable ? _table.U32(offset + 4 + (count * 2)) : uint.MaxValue;
        float[] values = new float[count];
        for (int i = 0; i < count; i++)
        {
            float value = format is 6 or 7 && i is 2 or 5 ? _table.U16(offset + 4 + (i * 2)) : _table.I16(offset + 4 + (i * 2));
            values[i] = value + Delta(variation, i);
        }

        int line = _table.Offset24(offset, offset + 1);
        PaintGraph2D graph = ColorLine(line, variable, (stops, extend) => format switch
        {
            4 or 5 => PaintGraph2D.LinearGradient(new(values[0], values[1]), new(values[2], values[3]), new(values[4], values[5]), stops, extend),
            6 or 7 => PaintGraph2D.RadialGradient(new(values[0], values[1]), values[2], new(values[3], values[4]), values[5], stops, extend),
            _ => PaintGraph2D.SweepGradient(new(values[0], values[1]), ((values[2] / 16384) + 1) * MathF.PI, ((values[3] / 16384) + 1) * MathF.PI, stops, extend),
        });
        return new Node(graph, null);
    }

    private PaintGraph2D ColorLine(int offset, bool variable, Func<ColorStop2D[], ExtendMode2D, PaintGraph2D> gradient)
    {
        int extendValue = _table.U8(offset);
        int count = _table.U16(offset + 1);
        if (extendValue > 2 || count > 4096)
        {
            throw new InvalidDataException("The COLR color line has invalid extension or stop count.");
        }

        int stride = variable ? 10 : 6;
        _table.Check(offset + 3, checked(count * stride));
        var stops = new List<(float Offset, Color2D Color, float ForegroundAlpha)>();
        bool usesForeground = false;
        for (int i = 0; i < count; i++)
        {
            int record = offset + 3 + (i * stride);
            uint variation = variable ? _table.U32(record + 6) : uint.MaxValue;
            float position = _table.F2(record) + (Delta(variation, 0) / 16384);
            float alpha = Math.Clamp(_table.F2(record + 4) + (Delta(variation, 1) / 16384), 0, 1);
            ushort palette = _table.U16(record + 2);
            if (palette == ushort.MaxValue)
            {
                usesForeground = true;
                stops.Add((position, default, alpha));
            }
            else
            {
                Color2D color = Color(palette);
                stops.Add((position, color with { A = color.A * alpha }, 0));
            }
        }

        (float Offset, Color2D Color, float ForegroundAlpha)[] ordered = stops.OrderBy(stop => stop.Offset).ToArray();
        var extend = (ExtendMode2D)extendValue;
        PaintGraph2D colors = gradient(ordered.Select(stop => new ColorStop2D(stop.Offset, stop.Color)).ToArray(), extend);
        if (!usesForeground)
        {
            return colors;
        }

        PaintGraph2D mask = gradient(ordered.Select(stop => new ColorStop2D(stop.Offset, new Color2D(1, 1, 1, stop.ForegroundAlpha))).ToArray(), extend);
        var foreground = PaintGraph2D.Composite(Foreground(), mask, (CompositeMode2D)5);
        return PaintGraph2D.Composite(colors, foreground, (CompositeMode2D)12);
    }

    private Matrix3x2 Transform(int offset, int format)
    {
        bool variable = (format & 1) != 0;
        if (format is 12 or 13)
        {
            int affine = _table.Offset24(offset, offset + 4);
            uint variation = variable ? _table.U32(affine + 24) : uint.MaxValue;
            float Value(int i) => _table.Fixed(affine + (i * 4)) + (Delta(variation, i) / 65536);
            return new Matrix3x2(Value(0), Value(1), Value(2), Value(3), Value(4), Value(5));
        }

        int baseFormat = format & ~1;
        int count = baseFormat switch { 14 or 16 or 28 => 2, 18 or 30 => 4, 20 or 24 => 1, 22 or 26 => 3, _ => throw new InvalidDataException("Invalid COLR transform format.") };
        uint index = variable ? _table.U32(offset + 4 + (count * 2)) : uint.MaxValue;
        float[] values = new float[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = _table.I16(offset + 4 + (i * 2)) + Delta(index, i);
        }

        if (baseFormat == 14)
        {
            return Matrix3x2.CreateTranslation(values[0], values[1]);
        }

        Matrix3x2 matrix;
        Vector2 center = default;
        if (baseFormat is 16 or 18 or 20 or 22)
        {
            bool uniform = baseFormat is 20 or 22;
            matrix = Matrix3x2.CreateScale(values[0] / 16384, uniform ? values[0] / 16384 : values[1] / 16384);
            if (baseFormat is 18 or 22)
            {
                int first = uniform ? 1 : 2;
                center = new(values[first], values[first + 1]);
            }
        }
        else if (baseFormat is 24 or 26)
        {
            matrix = Matrix3x2.CreateRotation(values[0] / 16384 * MathF.PI);
            if (baseFormat == 26)
            {
                center = new(values[1], values[2]);
            }
        }
        else
        {
            matrix = new Matrix3x2(1, MathF.Tan(values[1] / 16384 * MathF.PI), -MathF.Tan(values[0] / 16384 * MathF.PI), 1, 0, 0);
            if (baseFormat == 30)
            {
                center = new(values[2], values[3]);
            }
        }

        return Matrix3x2.CreateTranslation(-center) * matrix * Matrix3x2.CreateTranslation(center);
    }

    private Node ApplyClip(ushort glyphId, Node node)
    {
        if (_clipList == 0)
        {
            return node;
        }

        if (_table.U8(_clipList) != 1)
        {
            throw new InvalidDataException("The COLR ClipList format is unsupported.");
        }

        uint count = _table.U32(_clipList + 1);
        if (count > 65536 || count > (_table.Length - _clipList - 5) / 7)
        {
            throw new InvalidDataException("The COLR ClipList records are truncated.");
        }

        for (int i = 0; i < count; i++)
        {
            int record = _clipList + 5 + (i * 7);
            if (_table.U16(record) > _table.U16(record + 2))
            {
                throw new InvalidDataException("The COLR ClipList contains an inverted glyph range.");
            }

            if (glyphId < _table.U16(record) || glyphId > _table.U16(record + 2))
            {
                continue;
            }

            int box = _table.Offset24(_clipList, record + 4);
            int format = _table.U8(box);
            if (format is not 1 and not 2)
            {
                throw new InvalidDataException("The COLR ClipBox format is invalid.");
            }

            uint variation = format == 2 ? _table.U32(box + 9) : uint.MaxValue;
            float x0 = _table.I16(box + 1) + Delta(variation, 0);
            float y0 = _table.I16(box + 3) + Delta(variation, 1);
            float x1 = _table.I16(box + 5) + Delta(variation, 2);
            float y1 = _table.I16(box + 7) + Delta(variation, 3);
            var bounds = new Rect2D(x0, y0, MathF.Max(0, x1 - x0), MathF.Max(0, y1 - y0));
            return new Node(PaintGraph2D.Clip(Rectangle(bounds), node.Paint), Intersect(node.Bounds, bounds));
        }

        return node;
    }

    private (Path2D Path, Rect2D Bounds) Outline(ushort glyphId)
    {
        if (glyphId >= _glyphCount)
        {
            throw new InvalidDataException("PaintGlyph references an outline outside the font's glyph range.");
        }

        if (_outlines.TryGetValue(glyphId, out (Path2D Path, Rect2D Bounds) found))
        {
            return found;
        }

        var builder = new PathBuilder2D();
        Vector2 minimum = new(float.PositiveInfinity);
        Vector2 maximum = new(float.NegativeInfinity);
        using SKPath? path = _font.GetGlyphPath(glyphId);
        if (path is not null)
        {
            using SKPath.RawIterator iterator = path.CreateRawIterator();
            var points = new SKPoint[4];
            SKPathVerb verb;
            while ((verb = iterator.Next(points)) != SKPathVerb.Done)
            {
                Vector2 Point(int index)
                {
                    var point = new Vector2(points[index].X, -points[index].Y);
                    minimum = Vector2.Min(minimum, point);
                    maximum = Vector2.Max(maximum, point);
                    return point;
                }

                switch (verb)
                {
                    case SKPathVerb.Move: builder.MoveTo(Point(0)); break;
                    case SKPathVerb.Line: builder.LineTo(Point(1)); break;
                    case SKPathVerb.Quad: builder.QuadraticTo(Point(1), Point(2)); break;
                    case SKPathVerb.Cubic: builder.CubicTo(Point(1), Point(2), Point(3)); break;
                    case SKPathVerb.Close: builder.Close(); break;
                    default: throw new NotSupportedException("The outline contains a non-polynomial conic segment.");
                }
            }
        }

        Rect2D bounds = float.IsPositiveInfinity(minimum.X) ? default : new(minimum.X, minimum.Y, maximum.X - minimum.X, maximum.Y - minimum.Y);
        (Path2D Path, Rect2D Bounds) result = (builder.Build(), bounds);
        _outlines.Add(glyphId, result);
        return result;
    }

    private float Delta(uint index, int additional) => index == uint.MaxValue ? 0 : _variations.Delta(checked(index + (uint)additional));

    private PaintGraph2D ColorPaint(ushort index, float alpha)
        => index == ushort.MaxValue ? PaintGraph2D.Opacity(alpha, Foreground()) : PaintGraph2D.Solid(Color(index) with { A = Color(index).A * alpha });

    private PaintGraph2D Foreground()
    {
        if (!Matrix3x2.Invert(_coordinate, out Matrix3x2 inverse))
        {
            return PaintGraph2D.Solid(default);
        }

        return PaintGraph2D.Transform(inverse, _foreground);
    }

    private Color2D Color(ushort index)
    {
        if (index >= _paletteEntries)
        {
            throw new InvalidDataException("A COLR palette index is outside the selected CPAL palette.");
        }

        int offset = _paletteStart + (index * 4);
        return new Color2D(_palette.U8(offset + 2) / 255f, _palette.U8(offset + 1) / 255f, _palette.U8(offset) / 255f, _palette.U8(offset + 3) / 255f);
    }

    private sealed record Node(PaintGraph2D Paint, Rect2D? Bounds);
}
