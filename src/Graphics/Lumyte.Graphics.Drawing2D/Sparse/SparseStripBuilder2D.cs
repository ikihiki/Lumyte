using System.Numerics;
using Lumyte.Mathematics.Geometry;
using Point = Lumyte.Mathematics.Geometry.Point2D<double>;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>CPU-authoritative correlated4x4 shape preparation; never changes renderer or AA precision.</summary>
internal static class SparseStripBuilder2D
{
    internal const long DefaultMaxWorkingBytes = 256L * 1024 * 1024;
    private const long MaximumSampleEdges = 268435456;

    /// <summary>Builds strips without materializing retained recordings into a new public model.</summary>
    internal static SparseStripPlan2D Create(DrawList2D list, DrawingTargetInfo2D target, long maxWorkingBytes = DefaultMaxWorkingBytes, CancellationToken cancellationToken = default, int maxStrips = int.MaxValue, int maxBoundaryMasks = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxWorkingBytes);
        Check(checked(1024L + (list.Count * 512L)), maxWorkingBytes);
        long inputPeak = DrawingWorkingBudget2D.Validate(list, target, maxWorkingBytes);
        var commands = new DrawingCommand2D[list.Count];
        int index = 0;
        if (list.HasRetainedPages)
        {
            foreach (DrawList2D page in list.RetainedPages.Pages)
            {
                foreach (DrawingCommand2D command in page.Commands)
                {
                    commands[index++] = command;
                }
            }
        }
        else
        {
            foreach (DrawingCommand2D command in list.Commands)
            {
                commands[index++] = command;
            }
        }

        return Build(commands, target, maxWorkingBytes, cancellationToken, inputPeak, maxStrips, maxBoundaryMasks);
    }

    /// <summary>Builds one command for direct internal consumers.</summary>
    internal static SparseStripPlan2D Create(DrawingCommand2D command, DrawingTargetInfo2D target, long maxWorkingBytes = DefaultMaxWorkingBytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxWorkingBytes);
        Check(1024, maxWorkingBytes);
        long inputPeak = command is FillPathCommand2D or StrokePathCommand2D or DrawLineCommand2D or DrawMeshCommand2D
            ? DrawingWorkingBudget2D.Validate(new DrawList2D([command]), target, maxWorkingBytes)
            : 0;
        return Build([command], target, maxWorkingBytes, cancellationToken, inputPeak);
    }

    private static SparseStripPlan2D Build(DrawingCommand2D[] commands, DrawingTargetInfo2D target, long limit, CancellationToken token, long inputPeak, int maxStrips = int.MaxValue, int maxBoundaryMasks = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(target);
        token.ThrowIfCancellationRequested();
        if (!float.IsFinite(target.PixelScale) || target.PixelScale <= 0 || !float.IsFinite(target.PixelOrigin.X) || !float.IsFinite(target.PixelOrigin.Y)
            || target.PixelOrigin.X != MathF.Truncate(target.PixelOrigin.X) || target.PixelOrigin.Y != MathF.Truncate(target.PixelOrigin.Y))
        {
            throw new ArgumentException("Sparse strips require positive finite scale and integral finite origin.", nameof(target));
        }

        if (target.Description.Width == 0 || target.Description.Height == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(target));
        }

        if (target.Description.Width > 32768 || target.Description.Height > 32768)
        {
            throw new NotSupportedException("Sparse strips support target extents at most 32768.");
        }

        long owned = checked(1024L + (commands.Length * 512L));
        Check(owned, limit);
        var shapes = new Shape[commands.Length];
        long peak = Math.Max(owned, inputPeak), work = 0, geometryCells = 0;
        Check(peak, limit);
        Matrix3x2 pixels = Matrix3x2.CreateScale(target.PixelScale) * Matrix3x2.CreateTranslation(-target.PixelOrigin);
        for (int i = 0; i < commands.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            bool path = commands[i] is FillPathCommand2D or StrokePathCommand2D or DrawLineCommand2D;
            if (path || commands[i] is DrawMeshCommand2D)
            {
                Check(checked(owned + inputPeak), limit);
                peak = Math.Max(peak, checked(owned + inputPeak));
            }

            Shape shape = MakeShape(commands[i], commands[i].State.Transform * pixels, target);
            shapes[i] = shape;
            geometryCells = checked(geometryCells + (shape.Points.Length * 2L));
            if (geometryCells > 262144)
            {
                throw new NotSupportedException("Sparse shape geometry exceeds the aggregate 262144 Vector4 admission budget.");
            }

            owned = checked(owned + (shape.Points.Length * 32L));
            Check(owned, limit);
            peak = Math.Max(peak, owned);
            work = checked(work + ((long)(shape.Right - shape.Left) * (shape.Bottom - shape.Top) * 16 * Math.Max(1, shape.Points.Length)));
            if (work > MaximumSampleEdges)
            {
                throw new NotSupportedException("Sparse preparation exceeds the 268435456 sample-edge work budget.");
            }
        }

        // A bounded row scratch buffer is reused, never an area-sized mask. Two passes count then
        // write exact output capacities; every output array is admitted before allocation.
        int width = checked((int)target.Description.Width);
        owned = checked(owned + (width * 2L) + 64);
        Check(owned, limit);
        ushort[] row = new ushort[width];
        int stripCount = 0, maskCount = 0;
        Generate(shapes, row, null, null, ref stripCount, ref maskCount, token);
        if (stripCount > maxStrips || maskCount > maxBoundaryMasks)
        {
            throw new NotSupportedException("Sparse output exceeds explicit strip or boundary-mask limits.");
        }

        owned = checked(owned + (stripCount * 32L) + (maskCount * 2L) + 64);
        Check(owned, limit);
        peak = Math.Max(peak, owned);
        var strips = new SparseStrip2D[stripCount];
        ushort[] masks = new ushort[maskCount];
        stripCount = 0;
        maskCount = 0;
        Generate(shapes, row, strips, masks, ref stripCount, ref maskCount, token);
        return new(commands, strips, masks, peak);
    }

    private static void Generate(Shape[] shapes, ushort[] row, SparseStrip2D[]? strips, ushort[]? masks, ref int stripCount, ref int maskCount, CancellationToken token)
    {
        for (int ordinal = 0; ordinal < shapes.Length; ordinal++)
        {
            Shape shape = shapes[ordinal];
            for (int y = shape.Top; y < shape.Bottom; y++)
            {
                token.ThrowIfCancellationRequested();
                if (shape.Evaluation != SparseStripEvaluation2D.Samples || shape.FullTarget || (shape.AxisRectangle
                    && shape.Contains(shape.Left + 0.125, y + 0.125) && shape.Contains(shape.Right - 0.125, y + 0.875)))
                {
                    if (shape.Right > shape.Left)
                    {
                        if (strips is not null)
                        {
                            strips[stripCount] = new(ordinal, shape.Left, y, shape.Right - shape.Left, -1, shape.Evaluation);
                        }

                        stripCount = checked(stripCount + 1);
                    }

                    continue;
                }

                // Classify horizontal 16-pixel tiles without creating a full-target mask.
                for (int tile = shape.Left; tile < shape.Right; tile += 16)
                {
                    for (int x = tile; x < Math.Min(tile + 16, shape.Right); x++)
                    {
                        ushort bits = 0;
                        if (shape.Evaluation != SparseStripEvaluation2D.Samples)
                        {
                            bits = ushort.MaxValue;
                        }
                        else
                        {
                            for (int sy = 0; sy < 4; sy++)
                            {
                                for (int sx = 0; sx < 4; sx++)
                                {
                                    if (shape.Contains(x + ((sx + 0.5) / 4), y + ((sy + 0.5) / 4)))
                                    {
                                        bits |= (ushort)(1 << ((sy * 4) + sx));
                                    }
                                }
                            }
                        }

                        row[x] = bits;
                    }
                }

                int start = shape.Left;
                while (start < shape.Right)
                {
                    ushort bits = row[start];
                    if (bits == 0)
                    {
                        start++;
                        continue;
                    }

                    bool full = bits == ushort.MaxValue;
                    int end = start + 1;
                    while (end < shape.Right && row[end] != 0 && (row[end] == ushort.MaxValue) == full)
                    {
                        end++;
                    }

                    int offset = full ? -1 : maskCount;
                    if (!full)
                    {
                        if (masks is not null)
                        {
                            row.AsSpan(start, end - start).CopyTo(masks.AsSpan(maskCount));
                        }

                        maskCount = checked(maskCount + end - start);
                    }

                    if (strips is not null)
                    {
                        strips[stripCount] = new(ordinal, start, y, end - start, offset, shape.Evaluation);
                    }

                    stripCount = checked(stripCount + 1);
                    start = end;
                }
            }
        }
    }

    private static Shape MakeShape(DrawingCommand2D command, Matrix3x2 transform, DrawingTargetInfo2D target)
    {
        if (command is not (FillRectCommand2D or FillCircleCommand2D or FillRoundedRectCommand2D
            or FillPathCommand2D or StrokePathCommand2D or DrawLineCommand2D or DrawImageCommand2D
            or DrawCoverageMaskCommand2D or DrawDistanceFieldCommand2D or DrawMeshCommand2D or DrawPaintGraphCommand2D))
        {
            throw new NotSupportedException("The sparse backend does not support this command type.");
        }

        var shape = new Shape(transform);
        if (!shape.Invertible)
        {
            return shape;
        }

        switch (command)
        {
            case FillRectCommand2D rect:
                shape.Rect = rect.Rect;
                if (transform.M12 == 0 && transform.M21 == 0)
                {
                    shape.AxisRectangle = true;
                }
                else
                {
                    double x0 = rect.Rect.X, y0 = rect.Rect.Y;
                    double x1 = x0 + rect.Rect.Width, y1 = y0 + rect.Rect.Height;
                    Point a = TransformPoint(new(x0, y0), transform), b = TransformPoint(new(x1, y0), transform);
                    Point c = TransformPoint(new(x1, y1), transform), d = TransformPoint(new(x0, y1), transform);
                    shape.Points = [a, b, b, c, c, d, d, a];
                }

                break;
            case FillCircleCommand2D circle:
                shape.SetCircle(circle.Center, circle.Radius);
                shape.Radius = circle.Radius;
                break;
            case FillRoundedRectCommand2D rounded:
                shape.Rect = rounded.Rect;
                shape.Radius = Math.Min(rounded.Radius, Math.Min(rounded.Rect.Width, rounded.Rect.Height) * 0.5);
                break;
            case FillPathCommand2D path:
                shape.Rule = path.Rule;
                shape.Points = Edges(PathFlattening2D.Flatten(path.Path, transform), transform);
                break;
            case StrokePathCommand2D stroke:
                shape.Triangles = true;
                shape.Points = Triangles(PathFlattening2D.Stroke(PathFlattening2D.Flatten(stroke.Path, transform), stroke.Style, transform), transform);
                break;
            case DrawLineCommand2D line:
                var contour = new PathFlattening2D.Contour();
                contour.Points.Add(line.Start);
                contour.Points.Add(line.End);
                shape.Triangles = true;
                shape.Points = Triangles(PathFlattening2D.Stroke([contour], line.Style, transform), transform);
                break;
            case DrawImageCommand2D image:
                shape.Rect = image.Destination;
                shape.Evaluation = SparseStripEvaluation2D.Image;
                break;
            case DrawCoverageMaskCommand2D mask:
                shape.Rect = mask.Destination;
                shape.Evaluation = SparseStripEvaluation2D.CoverageMask;
                break;
            case DrawDistanceFieldCommand2D field:
                shape.Rect = field.Destination;
                shape.Evaluation = SparseStripEvaluation2D.DistanceField;
                break;
            case DrawMeshCommand2D mesh:
                if (mesh.Mesh.Vertices.Count > 65536)
                {
                    throw new NotSupportedException("Sparse mesh exceeds the 65536 vertex budget.");
                }

                shape.Evaluation = SparseStripEvaluation2D.MeshCenter;
                shape.Points = new Point[mesh.Mesh.Vertices.Count];
                for (int i = 0; i < shape.Points.Length; i++)
                {
                    shape.Points[i] = Transform(mesh.Mesh.Vertices[i].Position, transform);
                }

                break;
            case DrawPaintGraphCommand2D:
                shape.FullTarget = true;
                shape.Left = 0;
                shape.Top = 0;
                shape.Right = (int)target.Description.Width;
                shape.Bottom = (int)target.Description.Height;
                return shape;
            default:
                throw new NotSupportedException("The sparse backend does not support this command type.");
        }

        double left = double.PositiveInfinity, top = double.PositiveInfinity, right = double.NegativeInfinity, bottom = double.NegativeInfinity;
        if (command is FillPathCommand2D or StrokePathCommand2D or DrawLineCommand2D or DrawMeshCommand2D)
        {
            foreach (Point point in shape.Points)
            {
                Enclose(point);
            }
        }
        else if (shape.LocalRight > shape.LocalLeft && shape.LocalBottom > shape.LocalTop)
        {
            Enclose(TransformPoint(new(shape.LocalLeft, shape.LocalTop), transform));
            Enclose(TransformPoint(new(shape.LocalRight, shape.LocalTop), transform));
            Enclose(TransformPoint(new(shape.LocalLeft, shape.LocalBottom), transform));
            Enclose(TransformPoint(new(shape.LocalRight, shape.LocalBottom), transform));
        }

        if (right > left && bottom > top)
        {
            shape.Left = (int)Math.Clamp(Math.Floor(left), 0, target.Description.Width);
            shape.Top = (int)Math.Clamp(Math.Floor(top), 0, target.Description.Height);
            shape.Right = (int)Math.Clamp(Math.Ceiling(right), 0, target.Description.Width);
            shape.Bottom = (int)Math.Clamp(Math.Ceiling(bottom), 0, target.Description.Height);
        }

        return shape;

        void Enclose(Point point)
        {
            left = Math.Min(left, point.X);
            top = Math.Min(top, point.Y);
            right = Math.Max(right, point.X);
            bottom = Math.Max(bottom, point.Y);
        }
    }

    private static Point[] Edges(List<PathFlattening2D.Contour> contours, Matrix3x2 transform)
    {
        int count = 0;
        foreach (PathFlattening2D.Contour contour in contours)
        {
            count = checked(count + contour.Points.Count);
        }

        if (count > 65536)
        {
            throw new NotSupportedException("Sparse path exceeds the 65536 edge budget.");
        }

        var result = new Point[checked(count * 2)];
        int index = 0;
        foreach (PathFlattening2D.Contour contour in contours)
        {
            for (int i = 0; i < contour.Points.Count; i++)
            {
                result[index++] = Transform(contour.Points[i], transform);
                result[index++] = Transform(contour.Points[(i + 1) % contour.Points.Count], transform);
            }
        }

        return result;
    }

    private static Point[] Triangles(List<Vector4> vertices, Matrix3x2 transform)
    {
        var result = new Point[vertices.Count];
        for (int i = 0; i < vertices.Count; i++)
        {
            result[i] = Transform(new(vertices[i].X, vertices[i].Y), transform);
        }

        return result;
    }

    private static Point Transform(Vector2 point, Matrix3x2 transform) => TransformPoint(new(point.X, point.Y), transform);

    private static Point TransformPoint(Point point, Matrix3x2 transform)
    {
        try
        {
            Point result = Geometry2D.TransformAffineFused(point, transform);
            if (!double.IsFinite(result.X) || !double.IsFinite(result.Y))
            {
                throw new NotSupportedException("Sparse geometry requires finite transformed coordinates.");
            }

            return result;
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new NotSupportedException("Sparse geometry requires finite transformed coordinates.", exception);
        }
    }

    private static void Check(long bytes, long limit)
    {
        if (bytes > limit)
        {
            throw new NotSupportedException("Sparse CPU preparation exceeds MaxWorkingBytes.");
        }
    }

    private sealed class Shape
    {
        private readonly Matrix3x2 _transform;
        private readonly double _determinant;

        internal Shape(Matrix3x2 transform)
        {
            _transform = transform;
            _determinant = Math.FusedMultiplyAdd((double)transform.M11, transform.M22, -((double)transform.M12 * transform.M21));
            if (!double.IsFinite(_determinant) || !float.IsFinite(transform.M31) || !float.IsFinite(transform.M32))
            {
                throw new NotSupportedException("Sparse geometry requires finite transforms.");
            }
        }

        internal bool AxisRectangle { get; set; }

        internal bool FullTarget { get; set; }

        internal bool Invertible => _determinant != 0;

        internal Point[] Points { get; set; } = [];

        internal double LocalLeft { get; private set; }

        internal double LocalTop { get; private set; }

        internal double LocalRight { get; private set; }

        internal double LocalBottom { get; private set; }

        internal Rect2D Rect
        {
            set
            {
                LocalLeft = value.X;
                LocalTop = value.Y;
                LocalRight = (double)value.X + value.Width;
                LocalBottom = (double)value.Y + value.Height;
            }
        }

        internal double Radius { get; set; }

        internal bool Triangles { get; set; }

        internal FillRule2D Rule { get; set; }

        internal SparseStripEvaluation2D Evaluation { get; set; }

        internal int Left { get; set; }

        internal int Top { get; set; }

        internal int Right { get; set; }

        internal int Bottom { get; set; }

        internal void SetCircle(Vector2 center, float radius)
        {
            LocalLeft = (double)center.X - radius;
            LocalTop = (double)center.Y - radius;
            LocalRight = (double)center.X + radius;
            LocalBottom = (double)center.Y + radius;
        }

        internal bool Contains(double x, double y)
        {
            if (FullTarget)
            {
                return true;
            }

            if (AxisRectangle)
            {
                double ax = Math.FusedMultiplyAdd(LocalLeft, _transform.M11, _transform.M31);
                double bx = Math.FusedMultiplyAdd(LocalRight, _transform.M11, _transform.M31);
                double ay = Math.FusedMultiplyAdd(LocalTop, _transform.M22, _transform.M32);
                double by = Math.FusedMultiplyAdd(LocalBottom, _transform.M22, _transform.M32);
                return x >= Math.Min(ax, bx) && x < Math.Max(ax, bx) && y >= Math.Min(ay, by) && y < Math.Max(ay, by);
            }

            if (Points.Length != 0)
            {
                if (Triangles)
                {
                    for (int i = 0; i < Points.Length; i += 3)
                    {
                        int winding = Contribution(Points[i], Points[i + 1], x, y) + Contribution(Points[i + 1], Points[i + 2], x, y) + Contribution(Points[i + 2], Points[i], x, y);
                        if (winding != 0)
                        {
                            return true;
                        }
                    }

                    return false;
                }

                int sum = 0;
                for (int i = 0; i < Points.Length; i += 2)
                {
                    sum += Contribution(Points[i], Points[i + 1], x, y);
                }

                return Rule == FillRule2D.NonZero ? sum != 0 : (sum & 1) != 0;
            }

            double dx = x - _transform.M31, dy = y - _transform.M32;
            double localX = Math.FusedMultiplyAdd(dx, _transform.M22, -(dy * _transform.M21)) / _determinant;
            double localY = Math.FusedMultiplyAdd(dy, _transform.M11, -(dx * _transform.M12)) / _determinant;
            double right = LocalRight, bottom = LocalBottom;
            if (localX < LocalLeft || localX >= right || localY < LocalTop || localY >= bottom)
            {
                return false;
            }

            if (Radius <= 0)
            {
                return true;
            }

            double cornerX = localX - Math.Clamp(localX, LocalLeft + Radius, right - Radius);
            double cornerY = localY - Math.Clamp(localY, LocalTop + Radius, bottom - Radius);
            return Math.FusedMultiplyAdd(cornerX, cornerX, cornerY * cornerY) <= Radius * Radius;
        }

        private static int Contribution(Point a, Point b, double x, double y) => Geometry2D.GetWindingContributionFused(a.X, a.Y, b.X, b.Y, x, y);
    }
}
