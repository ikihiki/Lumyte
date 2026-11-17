using System.Numerics;

namespace Lumyte.Graphics.Drawing2D;

// No geometry arrays, normalization or path expansion; only budgeted identity tables grow.
internal static class DrawingWorkingBudget2D
{
    internal static long OwnedBytes(IEnumerable<IPreparedCommandBatch2D> batches, object? excludedOwner = null)
    {
        var owners = new Dictionary<object, long>(ReferenceEqualityComparer.Instance);
        foreach (IPreparedCommandBatch2D batch in batches)
        {
            if (batch is IRetainedMemory2D shared)
            {
                shared.AccountMemory(owners);
            }
            else if (batch is IRetainedCommandBatch2D retained)
            {
                owners.TryAdd(batch, retained.EstimatedBytes);
            }
        }

        if (excludedOwner is not null)
        {
            owners.Remove(excludedOwner);
        }

        long bytes = 0;
        foreach (long value in owners.Values)
        {
            bytes = checked(bytes + value);
        }

        return bytes;
    }

    internal static long Validate(DrawList2D content, DrawingTargetInfo2D target, long maximum)
    {
        try
        {
            return ValidateChecked(Commands(content), content.Count, target, maximum);
        }
        catch (OverflowException error)
        {
            throw new NotSupportedException("Input preparation working capacity overflow.", error);
        }
    }

    internal static long Validate<T>(ReadOnlyMemory<T> commands, DrawingTargetInfo2D target, long maximum)
        where T : DrawingCommand2D => ValidateChecked(Enumerate(commands), commands.Length, target, maximum);

    private static IEnumerable<DrawingCommand2D> Enumerate<T>(ReadOnlyMemory<T> commands)
        where T : DrawingCommand2D
    {
        for (int i = 0; i < commands.Length; i++)
        {
            yield return commands.Span[i];
        }
    }

    private static long ValidateChecked(IEnumerable<DrawingCommand2D> commands, int count, DrawingTargetInfo2D target, long maximum)
    {
        if (checked(4096L + (count * 512L)) > maximum)
        {
            throw new NotSupportedException("Command materialization exceeds the preallocation working budget.");
        }

        long bytes = 4096;
        Matrix3x2 pixels = Matrix3x2.CreateScale(target.PixelScale) * Matrix3x2.CreateTranslation(-target.PixelOrigin);
        Dictionary<PaintNode2D, HashSet<Matrix3x2>> paints = new(ReferenceEqualityComparer.Instance);
        HashSet<ClipStack2D> clips = new(ReferenceEqualityComparer.Instance);
        foreach (DrawingCommand2D command in commands)
        {
            paints.Clear(); // Each command can own a distinct lowered program.
            bytes = checked(bytes + 8192);
            Matrix3x2 transform = command.State.Transform * pixels;
            switch (command)
            {
                case FillPathCommand2D c:
                    CountPath(c.Path, transform, null);
                    break;
                case StrokePathCommand2D c:
                    CountPath(c.Path, transform, c.Style);
                    break;
                case DrawLineCommand2D c:
                    CountGeometry(2, 1, 1, c.Style, transform);
                    break;
            }

            if (command is FillPathCommand2D or StrokePathCommand2D or DrawLineCommand2D)
            {
                // Membership/output reservations include expanded stroke and clip bounds.
                Lumyte.Graphics.Abstractions.ScissorRect bounds = DrawingBounds2D.GetScissor(command, null, target, null, false, false);
                bytes = checked(bytes + ((long)bounds.Width * bounds.Height * 4));
            }

            if (command is DrawMeshCommand2D mesh)
            {
                bytes = checked(bytes + (mesh.Mesh.Indices.Count * 128L));
            }

            if (command.State.Clip.Elements.Count != 0 && !clips.Contains(command.State.Clip))
            {
                bytes = checked(bytes + 256);
                Check();
                clips.Add(command.State.Clip);
                foreach (ClipElement2D clip in command.State.Clip.Elements)
                {
                    CountPath(clip.Path, clip.Transform * pixels, null);
                }

                CountClipMask();
            }

            PaintNode2D? paint = command switch
            {
                FillRectCommand2D c => c.Paint.Root,
                FillRoundedRectCommand2D c => c.Paint.Root,
                FillCircleCommand2D c => c.Paint.Root,
                FillPathCommand2D c => c.Paint.Root,
                StrokePathCommand2D c => c.Paint.Root,
                DrawLineCommand2D c => c.Paint.Root,
                DrawCoverageMaskCommand2D c => c.Paint.Root,
                DrawDistanceFieldCommand2D c => c.Paint.Root,
                DrawMeshCommand2D c => c.Paint.Root,
                DrawPaintGraphCommand2D c => c.Graph.Root,
                _ => null,
            };
            if (paint is not null)
            {
                CountPaint(paint, transform, 0);
            }

            Check();
        }

        return bytes;

        void Check()
        {
            if (bytes > maximum)
            {
                throw new NotSupportedException("Input preparation exceeds its conservative preallocation working budget.");
            }
        }

        void CountClipMask()
        {
            if (target.Description.Width > 32768 || target.Description.Height > 32768)
            {
                throw new NotSupportedException("Clip normalization requires target dimensions at most 32768.");
            }

            // Bounded mask, normalization row scratch, upload and output copy.
            bytes = checked(bytes + (Math.Min(1048576L, (long)target.Description.Width * target.Description.Height) * 16)
                + (target.Description.Width * 32L) + 1024);
            Check();
        }

        void CountPath(Path2D path, Matrix3x2 transform, StrokeStyle2D? stroke)
        {
            // Raw input records also consume capacity (including redundant Close
            // verbs), even when they generate no flattened endpoints.
            bytes = checked(bytes + (path.Segments.Count * 384L));
            Check();
            long points = 0, contours = 0, edges = 0;
            Vector2 position = default, start = default;
            bool closed = false;
            foreach (PathSegment2D segment in path.Segments)
            {
                if (closed && segment.Verb is not PathVerb2D.Move and not PathVerb2D.Close)
                {
                    contours = checked(contours + 1);
                    points = checked(points + 1);
                    closed = false;
                }

                long added = 0;
                switch (segment.Verb)
                {
                    case PathVerb2D.Move:
                        contours = checked(contours + 1);
                        points = checked(points + 1);
                        position = start = segment.P0;
                        closed = false;
                        break;
                    case PathVerb2D.Line:
                        added = 1;
                        position = segment.P0;
                        break;
                    case PathVerb2D.Quadratic:
                        added = CurvePoints(position, segment.P0, segment.P1, segment.P1, false, transform);
                        position = segment.P1;
                        break;
                    case PathVerb2D.Cubic:
                        added = CurvePoints(position, segment.P0, segment.P1, segment.P2, true, transform);
                        position = segment.P2;
                        break;
                    case PathVerb2D.Close:
                        position = start;
                        closed = true;
                        break;
                    default:
                        throw new NotSupportedException("Unknown path verb in working budget.");
                }

                points = checked(points + added);
                edges = checked(edges + added);
                if (points > PathFlattening2D.MaximumEdges)
                {
                    throw new NotSupportedException("Path reservation exceeds the 65536 point budget.");
                }
            }

            // Include one explicit/implicit closing edge per contour. Open strokes
            // may not need it, but this also bounds closed contours without deduping.
            CountGeometry(points, checked(edges + contours), contours, stroke, transform);
        }

        void CountGeometry(long points, long edges, long contours, StrokeStyle2D? stroke, Matrix3x2 transform)
        {
            long triangles = 0;
            if (stroke is not null)
            {
                int round = 0;
                if (stroke.Cap == LineCap2D.Round || stroke.Join == LineJoin2D.Round)
                {
                    double half = stroke.Width * 0.5f;
                    double required = Math.Ceiling(Math.PI / Math.Acos(Math.Clamp(
                        1 - (0.125 / Math.Max(half * Scale(transform), 0.125)), -1, 1)));
                    if (!double.IsFinite(required) || required > 4096)
                    {
                        throw new NotSupportedException("Stroke round join exceeds the subdivision budget.");
                    }

                    round = Math.Max(8, (int)required);
                }

                long joins = stroke.Join == LineJoin2D.Round ? round : stroke.Join == LineJoin2D.Miter ? 2 : 1;
                long caps = stroke.Cap == LineCap2D.Round ? 3L * round : stroke.Cap == LineCap2D.Square ? 2 : 0;
                triangles = checked((2 * edges) + (joins * points) + (caps * contours));
                if (triangles > PathFlattening2D.MaximumEdges)
                {
                    throw new NotSupportedException("Stroke reservation exceeds the 65536 triangle budget.");
                }
            }

            // Point/contour lists with growth slack, filtered stroke points, Vector4
            // payload, lowering/copy/upload/device copies and registration overhead.
            // Capacity charges, not assertions about exact CLR heap sizes.
            bytes = checked(bytes + (contours * 256) + (points * 64) + (edges * 128) + (triangles * 384));
            Check();
        }

        void CountPaint(PaintNode2D node, Matrix3x2 transform, int depth)
        {
            if (depth > 128)
            {
                throw new NotSupportedException("Paint exceeds the preparation depth budget.");
            }

            // Compiled owners use reference identity AND the effective transform.
            if (paints.TryGetValue(node, out HashSet<Matrix3x2>? transforms) && transforms.Contains(transform))
            {
                return;
            }

            bytes = checked(bytes + 4096);
            Check();
            if (transforms is null)
            {
                transforms = [];
                paints.Add(node, transforms);
            }

            transforms.Add(transform);
            switch (node)
            {
                case LinearPaintNode2D c:
                    bytes = checked(bytes + (c.Stops.Count * 128L));
                    break;
                case RadialPaintNode2D c:
                    bytes = checked(bytes + (c.Stops.Count * 128L));
                    break;
                case SweepPaintNode2D c:
                    bytes = checked(bytes + (c.Stops.Count * 128L));
                    break;
                case CompositePaintNode2D c:
                    CountPaint(c.Source, transform, depth + 1);
                    CountPaint(c.Backdrop, transform, depth + 1);
                    break;
                case LayersPaintNode2D c:
                    foreach (PaintNode2D child in c.Children)
                    {
                        CountPaint(child, transform, depth + 1);
                    }

                    break;
                case OpacityPaintNode2D c:
                    CountPaint(c.Child, transform, depth + 1);
                    break;
                case TransformPaintNode2D c:
                    CountPaint(c.Child, c.Transform * transform, depth + 1);
                    break;
                case DistanceFieldPaintNode2D c:
                    CountPaint(c.Child, transform, depth + 1);
                    break;
                case ClipPaintNode2D c:
                    CountPath(c.Path, transform, null);
                    CountClipMask();
                    CountPaint(c.Child, transform, depth + 1);
                    break;
            }

            Check();
        }
    }

    private static float Scale(Matrix3x2 transform)
    {
        double squared = ((double)transform.M11 * transform.M11) + ((double)transform.M12 * transform.M12)
            + ((double)transform.M21 * transform.M21) + ((double)transform.M22 * transform.M22);
        return (float)Math.Min(float.MaxValue, Math.Sqrt(squared));
    }

    private static long CurvePoints(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, bool cubic, Matrix3x2 transform)
    {
        // Source flattening tests control distance to the finite chord and bisects
        // with float de Casteljau (depth 24). Second differences bound the distance
        // to a corresponding point ON the chord, including loops/backtracking.
        // Reserve a dyadic leaf count, not the uniform-N GPU subdivision count.
        double dx = (double)p0.X - (2.0 * p1.X) + p2.X;
        double dy = (double)p0.Y - (2.0 * p1.Y) + p2.Y;
        double second = Math.Sqrt((dx * dx) + (dy * dy));
        if (cubic)
        {
            dx = (double)p1.X - (2.0 * p2.X) + p3.X;
            dy = (double)p1.Y - (2.0 * p2.Y) + p3.Y;
            second = Math.Max(second, Math.Sqrt((dx * dx) + (dy * dy)));
        }

        double magnitude = Math.Max(
            Math.Max(Vector2.Abs(p0).X, Vector2.Abs(p0).Y),
            Math.Max(
                Math.Max(Vector2.Abs(p1).X, Vector2.Abs(p1).Y),
                Math.Max(Math.Max(Vector2.Abs(p2).X, Vector2.Abs(p2).Y), Math.Max(Vector2.Abs(p3).X, Vector2.Abs(p3).Y))));
        double tolerance = 0.125f / MathF.Max(Scale(transform), 0.0001f);

        // Cover midpoint/projection rounding across all 24 source levels. If this
        // guard alone consumes tolerance, refuse an unprovable small reservation.
        double remaining = tolerance - (magnitude * 128 * 1.1920928955078125e-7);
        if (!double.IsFinite(second) || !double.IsFinite(remaining) || remaining <= 0)
        {
            throw new NotSupportedException("Curve precision exceeds the preallocation budget.");
        }

        long count = 1;
        while (4 * second > remaining * count * count)
        {
            count = checked(count * 2);
            if (count > PathFlattening2D.MaximumEdges)
            {
                throw new NotSupportedException("Curve reservation exceeds the 65536 point budget.");
            }
        }

        return count;
    }

    private static IEnumerable<DrawingCommand2D> Commands(DrawList2D content)
    {
        if (content.HasRetainedPages)
        {
            foreach (DrawList2D page in content.RetainedPages.Pages)
            {
                foreach (DrawingCommand2D command in page.Commands)
                {
                    yield return command;
                }
            }
        }
        else
        {
            foreach (DrawingCommand2D command in content.Commands)
            {
                yield return command;
            }
        }
    }
}
