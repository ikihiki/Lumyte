using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Text.Parley.Interop;

namespace Lumyte.Graphics.Text.Parley;

internal static unsafe class ParleyLayoutImporter2D
{
    public static ParleyTextLayout2D Import(string text, Utf8Text2D source, LpLayoutView view, IReadOnlyList<PaintGraph2D> paints, IReadOnlyDictionary<uint, string> fontNames)
    {
        ReadOnlySpan<LpGlyph> nativeGlyphs = Buffer(view.glyphs, view.glyph_count);
        ReadOnlySpan<LpPathSegment> paths = Buffer(view.paths, view.path_count);
        ReadOnlySpan<LpDecoration> decorations = Buffer(view.decorations, view.decoration_count);
        var drawings = new List<TextDrawing2D>();
        var glyphs = new List<ParleyGlyph2D>(nativeGlyphs.Length);
        Rect2D? inkBounds = null;
        foreach (LpGlyph glyph in nativeGlyphs)
        {
            if (glyph.cluster_end < glyph.cluster_start || !fontNames.TryGetValue(glyph.font_id, out string? family))
            {
                throw new InvalidDataException("The layout backend returned an invalid glyph source or font.");
            }

            int utf16Start = source.Utf16Offset(glyph.cluster_start);
            int utf16End = source.Utf16Offset(glyph.cluster_end);
            PaintGraph2D paint = Brush(paints, glyph.brush_index);
            Matrix3x2 transform = Matrix3x2.CreateScale(glyph.scale, -glyph.scale) * Matrix3x2.CreateTranslation(glyph.x, glyph.y);
            bool color = (glyph.flags & 1) != 0;
            if (color)
            {
                var importer = new ParleyPaintImporter2D(view, transform, paint);
                ParleyPaintImporter2D.PaintEntry entry = importer.Import(glyph.paint_start, glyph.paint_count);
                if (entry.Bounds is not Rect2D bounds)
                {
                    throw new NotSupportedException("The color glyph has an unbounded paint graph; a finite glyph or rectangle clip is required.");
                }

                drawings.Add(new TextPaintGeometry2D(entry.Paint, bounds));
                if (bounds.Width > 0 && bounds.Height > 0)
                {
                    inkBounds = Union(inkBounds, bounds);
                }
            }
            else if (glyph.path_count != 0)
            {
                (Path2D path, Rect2D bounds) = Path(paths, glyph.path_start, glyph.path_count, transform);
                if (path.Segments.Count > 0)
                {
                    drawings.Add(new TextGeometry2D(path, paint));
                    inkBounds = Union(inkBounds, bounds);
                }
            }

            glyphs.Add(new ParleyGlyph2D(glyph.glyph_id, glyph.font_id, family, checked((int)glyph.cluster_start), checked((int)(glyph.cluster_end - glyph.cluster_start)), utf16Start, utf16End - utf16Start, new Vector2(glyph.x, glyph.y), glyph.advance, color));
        }

        foreach (LpDecoration decoration in decorations)
        {
            var bounds = new Rect2D(decoration.x, decoration.y, decoration.width, decoration.height);
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                continue;
            }

            drawings.Add(new TextGeometry2D(Rectangle(bounds, Matrix3x2.Identity).Path, Brush(paints, decoration.brush_index)));
            inkBounds = Union(inkBounds, bounds);
        }

        var layout = TextLayout2D.FromDrawings(text, inkBounds ?? default, new Rect2D(0, 0, view.width, view.height), checked((int)view.line_count), drawings);
        return new ParleyTextLayout2D(layout, glyphs);
    }

    public static ReadOnlySpan<T> Buffer<T>(T* data, nuint count)
        where T : unmanaged
    {
        if (count > 4000000 || (data is null && count != 0))
        {
            throw new InvalidDataException("The native layout buffer is invalid or exceeds the snapshot budget.");
        }

        return new ReadOnlySpan<T>(data, (int)count);
    }

    public static ReadOnlySpan<T> Range<T>(ReadOnlySpan<T> source, uint start, uint count)
    {
        if (start > source.Length || count > source.Length - start)
        {
            throw new InvalidDataException("The native layout array range is invalid.");
        }

        return source.Slice((int)start, (int)count);
    }

    public static (Path2D Path, Rect2D Bounds) Path(ReadOnlySpan<LpPathSegment> source, uint start, uint count, Matrix3x2 transform)
    {
        var builder = new PathBuilder2D();
        Vector2 minimum = new(float.PositiveInfinity);
        Vector2 maximum = new(float.NegativeInfinity);
        Vector2 Point(float x, float y)
        {
            var result = Vector2.Transform(new Vector2(x, y), transform);
            minimum = Vector2.Min(minimum, result);
            maximum = Vector2.Max(maximum, result);
            return result;
        }

        foreach (LpPathSegment segment in Range(source, start, count))
        {
            switch (segment.kind)
            {
                case 0:
                    builder.MoveTo(Point(segment.x0, segment.y0));
                    break;
                case 1:
                    builder.LineTo(Point(segment.x0, segment.y0));
                    break;
                case 2:
                    builder.QuadraticTo(Point(segment.x0, segment.y0), Point(segment.x1, segment.y1));
                    break;
                case 3:
                    builder.CubicTo(Point(segment.x0, segment.y0), Point(segment.x1, segment.y1), Point(segment.x2, segment.y2));
                    break;
                case 4:
                    builder.Close();
                    break;
                default:
                    throw new InvalidDataException("The native glyph outline contains an unknown path verb.");
            }
        }

        Rect2D bounds = float.IsPositiveInfinity(minimum.X) ? default : new Rect2D(minimum.X, minimum.Y, maximum.X - minimum.X, maximum.Y - minimum.Y);
        return (builder.Build(), bounds);
    }

    public static (Path2D Path, Rect2D Bounds) Rectangle(Rect2D bounds, Matrix3x2 transform)
    {
        var p0 = Vector2.Transform(new Vector2(bounds.X, bounds.Y), transform);
        var p1 = Vector2.Transform(new Vector2(bounds.X + bounds.Width, bounds.Y), transform);
        var p2 = Vector2.Transform(new Vector2(bounds.X + bounds.Width, bounds.Y + bounds.Height), transform);
        var p3 = Vector2.Transform(new Vector2(bounds.X, bounds.Y + bounds.Height), transform);
        var builder = new PathBuilder2D();
        builder.MoveTo(p0);
        builder.LineTo(p1);
        builder.LineTo(p2);
        builder.LineTo(p3);
        builder.Close();
        var minimum = Vector2.Min(Vector2.Min(p0, p1), Vector2.Min(p2, p3));
        var maximum = Vector2.Max(Vector2.Max(p0, p1), Vector2.Max(p2, p3));
        return (builder.Build(), new Rect2D(minimum.X, minimum.Y, maximum.X - minimum.X, maximum.Y - minimum.Y));
    }

    public static Rect2D Union(Rect2D? first, Rect2D second)
    {
        if (first is not Rect2D a)
        {
            return second;
        }

        if (a.Width == 0 || a.Height == 0)
        {
            return second;
        }

        if (second.Width == 0 || second.Height == 0)
        {
            return a;
        }

        float x = MathF.Min(a.X, second.X);
        float y = MathF.Min(a.Y, second.Y);
        return new Rect2D(x, y, MathF.Max(a.X + a.Width, second.X + second.Width) - x, MathF.Max(a.Y + a.Height, second.Y + second.Height) - y);
    }

    public static Rect2D? Intersect(Rect2D? first, Rect2D? second)
    {
        if (first is not Rect2D a)
        {
            return second;
        }

        if (second is not Rect2D b)
        {
            return first;
        }

        float x = MathF.Max(a.X, b.X);
        float y = MathF.Max(a.Y, b.Y);
        return new Rect2D(x, y, MathF.Max(0, MathF.Min(a.X + a.Width, b.X + b.Width) - x), MathF.Max(0, MathF.Min(a.Y + a.Height, b.Y + b.Height) - y));
    }

    private static PaintGraph2D Brush(IReadOnlyList<PaintGraph2D> paints, uint index)
        => index < paints.Count ? paints[(int)index] : throw new InvalidDataException("The native glyph references an unknown style paint.");
}
