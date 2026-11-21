using System.Numerics;
using Lumyte.Mathematics.Geometry;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

// Counting uses the exact flattening primitive and tolerance before any contour/triangle allocation.
internal sealed class CanonicalAdmission2D(SceneLimits2D limits, DrawingTargetInfo2D target, long maxHostBytes = long.MaxValue)
{
    private readonly Dictionary<PaintNode2D, HashSet<Matrix3x2>> _paintSeen = new(ReferenceEqualityComparer.Instance);
    private long _contours;
    private long _segments;
    private long _triangles;
    private long _tiles;
    private long _commands;
    private long _paintNodes;
    private long _stopCells;
    private long _clipMasks;

    internal long CanonicalCellBound
        => checked(1 + (_commands * 6) + (((_paintNodes * 3) + (_commands * 8)) * 8) + _segments + _contours + (_triangles * 3) + _stopCells +
        (_clipMasks * (((checked((long)target.Description.Width * target.Description.Height) + 3) / 4) + 2)));

    internal long PeakHostBytes => checked(4096 + (_paintNodes * 256) + (128 * (CanonicalCellBound + _segments + _contours)));

    internal void Inspect(IReadOnlyList<DrawingCommand2D> commands)
    {
        Matrix3x2 pixels = Matrix3x2.CreateScale(target.PixelScale) * Matrix3x2.CreateTranslation(-target.PixelOrigin);
        foreach (DrawingCommand2D c in commands)
        {
            _paintSeen.Clear();
            _commands = checked(_commands + 1);
            if (c.State.Clip.Elements.Any(clip => !AnalyticRectangle(clip.Path, clip.Transform * pixels)))
            {
                _clipMasks = checked(_clipMasks + 2);
            }

            _tiles = checked(_tiles + ((((long)target.Description.Width + 15) / 16) * (((long)target.Description.Height + 15) / 16)));
            Check();
            Matrix3x2 t = c.State.Transform * pixels;
            foreach (ClipElement2D clip in c.State.Clip.Elements)
            {
                Count(clip.Path, clip.Transform * pixels, null);
            }

            PaintGraph2D? paint = c switch
            {
                FillRectCommand2D x => x.Paint,
                FillRoundedRectCommand2D x => x.Paint,
                FillCircleCommand2D x => x.Paint,
                DrawLineCommand2D x => x.Paint,
                FillPathCommand2D x => x.Paint,
                StrokePathCommand2D x => x.Paint,
                DrawCoverageMaskCommand2D x => x.Paint,
                DrawDistanceFieldCommand2D x => x.Paint,
                DrawMeshCommand2D x => x.Paint,
                DrawPaintGraphCommand2D x => x.Graph,
                _ => null,
            };
            if (paint is not null)
            {
                InspectPaint(paint.Root, t, 0);
            }

            switch (c)
            {
                case FillPathCommand2D x: Count(x.Path, t, null); break;
                case StrokePathCommand2D x: Count(x.Path, t, x.Style); break;
                case DrawLineCommand2D x:
                    var b = new PathBuilder2D(); b.MoveTo(x.Start); b.LineTo(x.End); Count(b.Build(), t, x.Style); break;
                case DrawMeshCommand2D x: _triangles = checked(_triangles + (x.Mesh.Indices.Count / 3)); Check(); break;
            }
        }
    }

    private static bool AnalyticRectangle(Path2D path, Matrix3x2 transform)
        => DrawingProgram2D.TryRectangle(path, out _) && ((transform.M12 == 0 && transform.M21 == 0) || (transform.M11 == 0 && transform.M22 == 0));

    private void InspectPaint(PaintNode2D p, Matrix3x2 t, int depth)
    {
        if (depth > 64)
        {
            throw new NotSupportedException("Paint graph exceeds 64 levels.");
        }

        if (_paintSeen.TryGetValue(p, out HashSet<Matrix3x2>? transforms) && transforms.Contains(t))
        {
            return;
        }

        _paintNodes = checked(_paintNodes + 1);
        if (_paintNodes > maxHostBytes / 256)
        {
            throw new NotSupportedException("Paint metadata analysis exceeds the finite host ceiling before allocation.");
        }

        if (transforms is null)
        {
            transforms = [];
            _paintSeen.Add(p, transforms);
        }

        transforms.Add(t);
        long stopCells = p switch
        {
            LinearPaintNode2D linear => 2L * linear.Stops.Count,
            RadialPaintNode2D radial => 2L * radial.Stops.Count,
            SweepPaintNode2D sweep => 2L * sweep.Stops.Count,
            _ => 0,
        };
        _stopCells = checked(_stopCells + stopCells);
        if (p is ClipPaintNode2D clip && !AnalyticRectangle(clip.Path, t))
        {
            _clipMasks = checked(_clipMasks + 1);
        }

        switch (p)
        {
            case ClipPaintNode2D x: Count(x.Path, t, null); InspectPaint(x.Child, t, depth + 1); break;
            case TransformPaintNode2D x: InspectPaint(x.Child, x.Transform * t, depth + 1); break;
            case OpacityPaintNode2D x: InspectPaint(x.Child, t, depth + 1); break;
            case DistanceFieldPaintNode2D x: InspectPaint(x.Child, t, depth + 1); break;
            case CompositePaintNode2D x: InspectPaint(x.Source, t, depth + 1); InspectPaint(x.Backdrop, t, depth + 1); break;
            case LayersPaintNode2D x:
                foreach (PaintNode2D child in x.Children)
                {
                    InspectPaint(child, t, depth + 1);
                }

                break;
        }
    }

    private void Count(Path2D path, Matrix3x2 transform, StrokeStyle2D? style)
    {
        double squared = ((double)transform.M11 * transform.M11) + ((double)transform.M12 * transform.M12) + ((double)transform.M21 * transform.M21) + ((double)transform.M22 * transform.M22);
        float scale = (float)Math.Min(float.MaxValue, Math.Sqrt(squared));
        float tolerance = 0.125f / MathF.Max(scale, 0.0001f);
        int divisions = 8;
        if (style is not null && (style.Cap == LineCap2D.Round || style.Join == LineJoin2D.Round))
        {
            double required = Math.Ceiling(Math.PI / Math.Acos(Math.Clamp(1 - (0.125 / Math.Max((double)style.Width * 0.5 * scale, 0.125)), -1, 1)));
            if (!double.IsFinite(required) || required > 4096)
            {
                throw new NotSupportedException("Stroke subdivision budget exceeded.");
            }

            divisions = Math.Max(8, (int)required);
        }

        var sink = new Counter(this, style, divisions);
        bool closed = false;
        foreach (PathSegment2D segment in path.Segments)
        {
            if (closed && segment.Verb is not PathVerb2D.Move and not PathVerb2D.Close)
            {
                _contours = checked(_contours + 1);
                sink.Append(sink.Position);
                closed = false;
            }

            switch (segment.Verb)
            {
                case PathVerb2D.Move: _contours = checked(_contours + 1); Check(); sink.Append(segment.P0); sink.Start = segment.P0; closed = false; break;
                case PathVerb2D.Line: sink.Append(segment.P0); break;
                case PathVerb2D.Quadratic: Geometry2D.AppendFlattenedQuadraticBcl(sink.Position, segment.P0, segment.P1, tolerance, ref sink, 24); break;
                case PathVerb2D.Cubic: Geometry2D.AppendFlattenedCubicBcl(sink.Position, segment.P0, segment.P1, segment.P2, tolerance, ref sink, 24); break;
                case PathVerb2D.Close: closed = true; sink.Position = sink.Start; break;
            }
        }
    }

    private void Check()
    {
        if (PeakHostBytes > maxHostBytes)
        {
            throw new NotSupportedException("Canonical scene scratch exceeds the explicit host ceiling before expansion.");
        }

        if (_contours > limits.MaxContours || _segments > limits.MaxSegments || _triangles > limits.MaxStrokeTriangles || _tiles > limits.MaxTileReferences)
        {
            throw new NotSupportedException("Canonical scene expansion exceeds the declared finite limits before allocation.");
        }
    }

    private struct Counter(CanonicalAdmission2D owner, StrokeStyle2D? style, int divisions) : Geometry2D.IStreamingPointSink
    {
        internal Vector2 Position;
        internal Vector2 Start;

        public void Append(Vector2 point)
        {
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y))
            {
                throw new NotSupportedException("Nonfinite path coordinate.");
            }

            owner._segments = checked(owner._segments + 1);

            // Conservative per-point charge includes both caps, a full round join and segment body.
            if (style is not null)
            {
                owner._triangles = checked(owner._triangles + 2 + (style.Join == LineJoin2D.Round ? divisions : 2) + (style.Cap == LineCap2D.Round ? 2 * divisions : 2));
            }

            owner.Check();
            Position = point;
        }
    }
}
