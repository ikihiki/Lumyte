using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Mathematics.Geometry;
using Edge = Lumyte.Mathematics.Geometry.Segment2D<double>;
using Point = Lumyte.Mathematics.Geometry.Point2D<double>;

namespace Lumyte.Graphics.Drawing2D;

internal enum NormalizedClipKind2D
{
    None,
    Empty,
    Scissor,
    Analytic,
    SampleMask,
}

internal sealed record NormalizedClip2D(NormalizedClipKind2D Kind, ScissorRect Bounds, Vector4 Samples, Vector4[] Mask)
{
    internal static NormalizedClip2D Create(IReadOnlyList<ClipElement2D> elements, DrawingTargetInfo2D target, bool pixelSpace = false)
    {
        const int MaximumMaskPixels = 1048576;
        const long MaximumSampleEdges = 268435456;
        uint width = target.Description.Width, height = target.Description.Height;
        if (elements.Count > 4096)
        {
            throw new NotSupportedException("Clip normalization exceeds the 4096 captured element budget.");
        }

        ScissorRect full = new(0, 0, width, height);
        if (elements.Count == 0)
        {
            return new(NormalizedClipKind2D.None, full, default, []);
        }

        if (width > 32768 || height > 32768)
        {
            throw new NotSupportedException("Clip normalization requires target dimensions at most 32768.");
        }

        Matrix3x2 pixels = Matrix3x2.CreateScale(target.PixelScale) * Matrix3x2.CreateTranslation(-target.PixelOrigin);
        List<(Edge[] Edges, FillRule2D Rule)> paths = [];
        Bounds2D<double> clipBounds = new(0, 0, width, height);
        bool rectangles = true;
        Bounds2D<double> rectangleBounds = clipBounds;
        long edgeCount = 0;
        foreach (ClipElement2D element in elements)
        {
            Matrix3x2 transform = pixelSpace ? element.Transform : element.Transform * pixels;
            if (!Matrix3x2.Invert(transform, out Matrix3x2 inverse) || !Finite(inverse))
            {
                return new(NormalizedClipKind2D.Empty, default, default, []);
            }

            bool rectangle = DrawingProgram2D.TryRectangle(element.Path, out Rect2D rect)
                && transform.M12 == 0 && transform.M21 == 0;
            rectangles &= rectangle;
            if (rectangle)
            {
                Point a = Transform(new(rect.X, rect.Y), transform);
                Point b = Transform(new(rect.X + rect.Width, rect.Y + rect.Height), transform);
                (double Left, double Top, double Right, double Bottom) rectangleEdges = Geometry2D.EncloseBoundsEdges((a.X, a.Y, a.X, a.Y), (b.X, b.Y, b.X, b.Y));
                rectangleBounds = rectangleBounds.Intersect(new(rectangleEdges.Left, rectangleEdges.Top, rectangleEdges.Right, rectangleEdges.Bottom));
                continue;
            }

            List<Edge> edges = [];
            Bounds2D<double> pathBounds = Bounds2D<double>.Empty;
            foreach (PathFlattening2D.Contour contour in PathFlattening2D.Flatten(element.Path, transform))
            {
                for (int i = 0; i < contour.Points.Count; i++)
                {
                    Point a = Transform(contour.Points[i], transform);
                    Point b = Transform(contour.Points[(i + 1) % contour.Points.Count], transform);
                    if (a != b)
                    {
                        if (edgeCount + edges.Count >= PathFlattening2D.MaximumEdges)
                        {
                            throw new NotSupportedException("Clip normalization exceeds the 65536 aggregate edge budget.");
                        }

                        (double Left, double Top, double Right, double Bottom) edgeBounds = Geometry2D.EncloseBoundsEdges((a.X, a.Y, a.X, a.Y), (b.X, b.Y, b.X, b.Y));
                        (double Left, double Top, double Right, double Bottom) enclosed = Geometry2D.EncloseBoundsEdges((pathBounds.Left, pathBounds.Top, pathBounds.Right, pathBounds.Bottom), edgeBounds);
                        pathBounds = new(enclosed.Left, enclosed.Top, enclosed.Right, enclosed.Bottom);
                        edges.Add(new(a, b));
                    }
                }
            }

            if (edges.Count == 0)
            {
                return new(NormalizedClipKind2D.Empty, default, default, []);
            }

            clipBounds = clipBounds.Intersect(pathBounds);
            paths.Add((edges.ToArray(), element.Rule));
            edgeCount = checked(edgeCount + edges.Count);
        }

        clipBounds = clipBounds.Intersect(rectangleBounds);
        (double left, double top, double right, double bottom) = clipBounds;
        if (right <= left || bottom <= top)
        {
            return new(NormalizedClipKind2D.Empty, default, default, []);
        }

        if (rectangles)
        {
            int x0 = SampleIndex(left, width), y0 = SampleIndex(top, height);
            int x1 = SampleIndex(right, width), y1 = SampleIndex(bottom, height);
            if (x1 <= x0 || y1 <= y0)
            {
                return new(NormalizedClipKind2D.Empty, default, default, []);
            }

            ScissorRect bounds = new((uint)(x0 / 4), (uint)(y0 / 4), (uint)(((x1 + 3) / 4) - (x0 / 4)), (uint)(((y1 + 3) / 4) - (y0 / 4)));
            NormalizedClipKind2D kind = (x0 | y0 | x1 | y1) % 4 == 0
                ? bounds == full ? NormalizedClipKind2D.None : NormalizedClipKind2D.Scissor : NormalizedClipKind2D.Analytic;
            return new(kind, bounds, new(x0, y0, x1, y1), []);
        }

        uint x = (uint)Math.Clamp(Math.Floor(left), 0, width);
        uint y = (uint)Math.Clamp(Math.Floor(top), 0, height);
        uint endX = (uint)Math.Clamp(Math.Ceiling(right), x, width);
        uint endY = (uint)Math.Clamp(Math.Ceiling(bottom), y, height);
        int maskWidth = checked((int)(endX - x)), maskHeight = checked((int)(endY - y));
        long area = (long)maskWidth * maskHeight;
        if (area > MaximumMaskPixels || checked(area * 16 * (edgeCount + 1)) > MaximumSampleEdges)
        {
            throw new NotSupportedException("Normalized clip exceeds the 1048576 pixel or 268435456 sample-edge budget.");
        }

        var mask = new Vector4[(int)((area + 3) / 4)];
        bool any = false, all = true;
        for (int row = 0; row < maskHeight; row++)
        {
            for (int column = 0; column < maskWidth; column++)
            {
                uint bits = 0;
                for (int sy = 0; sy < 4; sy++)
                {
                    for (int sx = 0; sx < 4; sx++)
                    {
                        var sample = new Point(x + column + ((sx + 0.5) / 4), y + row + ((sy + 0.5) / 4));
                        bool covered = rectangleBounds.Contains(sample.X, sample.Y);
                        foreach ((Edge[] edges, FillRule2D rule) in paths)
                        {
                            if (!covered || !Contains(edges, rule, sample))
                            {
                                covered = false;
                                break;
                            }
                        }

                        if (covered)
                        {
                            bits |= 1u << ((sy * 4) + sx);
                        }
                    }
                }

                any |= bits != 0;
                all &= bits == 65535;
                int index = (row * maskWidth) + column;
                Vector4 vector = mask[index / 4];
                vector[index % 4] = bits;
                mask[index / 4] = vector;
            }
        }

        ScissorRect maskBounds = new(x, y, (uint)maskWidth, (uint)maskHeight);
        if (!any)
        {
            return new(NormalizedClipKind2D.Empty, default, default, []);
        }

        if (all)
        {
            return new(maskBounds == full ? NormalizedClipKind2D.None : NormalizedClipKind2D.Scissor, maskBounds, default, []);
        }

        return new(NormalizedClipKind2D.SampleMask, maskBounds, default, mask);
    }

    private static int SampleIndex(double edge, uint extent) => (int)Math.Clamp(Math.Ceiling((edge * 4) - 0.5), 0, extent * 4);

    private static bool Finite(Matrix3x2 m) => float.IsFinite(m.M11) && float.IsFinite(m.M12) && float.IsFinite(m.M21)
        && float.IsFinite(m.M22) && float.IsFinite(m.M31) && float.IsFinite(m.M32);

    private static Point Transform(Vector2 p, Matrix3x2 m)
    {
        try
        {
            return Geometry2D.TransformAffineFused(new Point(p.X, p.Y), m);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new NotSupportedException("Clip normalization requires finite transformed coordinates.", exception);
        }
    }

    private static bool Contains(Edge[] edges, FillRule2D rule, Point p)
    {
        int winding = Geometry2D.AccumulateWindingFused<double>(edges, p);
        return rule == FillRule2D.NonZero ? winding != 0 : (Math.Abs(winding) % 2) != 0;
    }
}
