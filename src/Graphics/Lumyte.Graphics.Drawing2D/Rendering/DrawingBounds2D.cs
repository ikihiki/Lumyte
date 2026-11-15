using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Mathematics.Geometry;

namespace Lumyte.Graphics.Drawing2D;

internal static class DrawingBounds2D
{
    internal static ScissorRect GetScissor(DrawingCommand2D? command, DrawingState2D? clip, DrawingTargetInfo2D target, Rect2D? source, bool hasOriginal, bool replace, ScissorRect? normalized = null)
    {
        Vector2 minimum = Vector2.Zero;
        Vector2 maximum = new(target.Description.Width, target.Description.Height);
        Matrix3x2 pixels = Matrix3x2.CreateScale(target.PixelScale) * Matrix3x2.CreateTranslation(-target.PixelOrigin);
        DrawingState2D? state = command?.State ?? clip;
        if (command is not null)
        {
            Rect2D? bounds = command switch
            {
                FillRectCommand2D c => c.Rect,
                FillRoundedRectCommand2D c => c.Rect,
                FillCircleCommand2D c => new(c.Center.X - c.Radius, c.Center.Y - c.Radius, c.Radius * 2, c.Radius * 2),
                DrawLineCommand2D c => Outset(LineBounds(c.Start, c.End), c.Style.Width * (c.Style.Cap == LineCap2D.Square ? 0.707107f : 0.5f)),
                FillPathCommand2D c => PathBounds(c.Path),
                StrokePathCommand2D c => Outset(PathBounds(c.Path), c.Style.Width * MathF.Max(c.Style.Cap == LineCap2D.Square ? 1.414214f : 1, c.Style.MiterLimit) * 0.5f),
                DrawImageCommand2D c => c.Destination,
                DrawCoverageMaskCommand2D c => c.Destination,
                DrawDistanceFieldCommand2D c => c.Destination,
                DrawMeshCommand2D c => MeshBounds(c.Mesh),
                _ => null,
            };
            if (bounds is not null)
            {
                Intersect(bounds.Value, command.State.Transform * pixels);
            }
        }
        else if (source is not null && !replace && !hasOriginal)
        {
            Intersect(source.Value, Matrix3x2.CreateTranslation(-target.PixelOrigin));
        }

        if (normalized is ScissorRect exact)
        {
            minimum = Vector2.Max(minimum, new(exact.X, exact.Y));
            maximum = Vector2.Min(maximum, new(exact.X + exact.Width, exact.Y + exact.Height));
        }
        else if (state is not null)
        {
            IReadOnlyList<ClipElement2D> elements = state.Clip.Elements;
            for (int i = 0; i < elements.Count; i++)
            {
                ClipElement2D element = elements[i];
                Intersect(PathBounds(element.Path), element.Transform * pixels);
            }
        }

        uint x = (uint)Math.Clamp(MathF.Floor(minimum.X), 0, target.Description.Width);
        uint y = (uint)Math.Clamp(MathF.Floor(minimum.Y), 0, target.Description.Height);
        uint right = (uint)Math.Clamp(MathF.Ceiling(maximum.X), x, target.Description.Width);
        uint bottom = (uint)Math.Clamp(MathF.Ceiling(maximum.Y), y, target.Description.Height);
        return new(x, y, right - x, bottom - y);

        void Intersect(Rect2D bounds, Matrix3x2 transform)
        {
            Geometry2D.GetTransformedBoundsBcl(new(bounds.X, bounds.Y), new(bounds.X + bounds.Width, bounds.Y + bounds.Height), transform, out Vector2 lower, out Vector2 upper);

            // Retain the previous extent reconstruction and its finite-range check.
            Rect2D transformed = float.IsPositiveInfinity(lower.X) ? default : new(lower.X, lower.Y, upper.X - lower.X, upper.Y - lower.Y);
            if (!float.IsFinite(transformed.X) || !float.IsFinite(transformed.Y) || !float.IsFinite(transformed.Width) || !float.IsFinite(transformed.Height))
            {
                throw new NotSupportedException("Transformed drawing bounds exceed the finite coordinate range.");
            }

            minimum = Vector2.Max(minimum, new(transformed.X - 1, transformed.Y - 1));
            maximum = Vector2.Min(maximum, new(transformed.X + transformed.Width + 1, transformed.Y + transformed.Height + 1));
        }
    }

    private static Rect2D PathBounds(Path2D path)
    {
        Vector2 minimum = new(float.PositiveInfinity);
        Vector2 maximum = new(float.NegativeInfinity);
        for (int i = 0; i < path.Segments.Count; i++)
        {
            PathSegment2D segment = path.Segments[i];
            if (segment.Verb != PathVerb2D.Close)
            {
                Geometry2D.IncludeInBoundsBcl(segment.P0, ref minimum, ref maximum);
            }

            if (segment.Verb is PathVerb2D.Quadratic or PathVerb2D.Cubic)
            {
                Geometry2D.IncludeInBoundsBcl(segment.P1, ref minimum, ref maximum);
            }

            if (segment.Verb == PathVerb2D.Cubic)
            {
                Geometry2D.IncludeInBoundsBcl(segment.P2, ref minimum, ref maximum);
            }
        }

        return Bounds(minimum, maximum);
    }

    private static Rect2D MeshBounds(Mesh2D mesh)
    {
        Vector2 minimum = new(float.PositiveInfinity);
        Vector2 maximum = new(float.NegativeInfinity);
        for (int i = 0; i < mesh.Vertices.Count; i++)
        {
            Geometry2D.IncludeInBoundsBcl(mesh.Vertices[i].Position, ref minimum, ref maximum);
        }

        return Bounds(minimum, maximum);
    }

    private static Rect2D LineBounds(Vector2 start, Vector2 end)
    {
        Vector2 minimum = new(float.PositiveInfinity);
        Vector2 maximum = new(float.NegativeInfinity);
        Geometry2D.IncludeInBoundsBcl(start, ref minimum, ref maximum);
        Geometry2D.IncludeInBoundsBcl(end, ref minimum, ref maximum);
        return Bounds(minimum, maximum);
    }

    private static Rect2D Bounds(Vector2 minimum, Vector2 maximum) => float.IsPositiveInfinity(minimum.X) ? default : new(minimum.X, minimum.Y, maximum.X - minimum.X, maximum.Y - minimum.Y);

    private static Rect2D Outset(Rect2D rect, float amount) => new(rect.X - amount, rect.Y - amount, rect.Width + (amount * 2), rect.Height + (amount * 2));
}
