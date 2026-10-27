using System.Numerics;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.Graphics.Text.Parley.Tests;

internal static class PaintAssertions
{
    public static IEnumerable<PaintNode2D> Nodes(PaintNode2D root)
    {
        var pending = new Stack<PaintNode2D>();
        pending.Push(root);
        while (pending.TryPop(out PaintNode2D? node))
        {
            yield return node;
            switch (node)
            {
                case ClipPaintNode2D clip:
                    pending.Push(clip.Child);
                    break;
                case TransformPaintNode2D transform:
                    pending.Push(transform.Child);
                    break;
                case OpacityPaintNode2D opacity:
                    pending.Push(opacity.Child);
                    break;
                case CompositePaintNode2D composite:
                    pending.Push(composite.Backdrop);
                    pending.Push(composite.Source);
                    break;
            }
        }
    }

    public static Vector4 Evaluate(PaintNode2D node, Vector2 point)
    {
        return node switch
        {
            SolidPaintNode2D solid => Premultiply(solid.Color),
            OpacityPaintNode2D opacity => Evaluate(opacity.Child, point) * opacity.Opacity,
            TransformPaintNode2D transform => Matrix3x2.Invert(transform.Transform, out Matrix3x2 inverse) ? Evaluate(transform.Child, Vector2.Transform(point, inverse)) : Vector4.Zero,
            ClipPaintNode2D clip => Contains(clip.Path, point, clip.Rule) ? Evaluate(clip.Child, point) : Vector4.Zero,
            CompositePaintNode2D composite => Composite(Evaluate(composite.Source, point), Evaluate(composite.Backdrop, point), composite.Mode),
            LinearPaintNode2D linear => EvaluateLinear(linear, point),
            _ => throw new InvalidOperationException("The current-color reference evaluator only accepts line clips, affine transforms, opacity, linear gradients and Porter-Duff operations."),
        };
    }

    private static Vector4 Premultiply(Color2D color) => new(Linear(color.R) * color.A, Linear(color.G) * color.A, Linear(color.B) * color.A, color.A);

    private static float Linear(float value) => value <= 0.04045f ? value / 12.92f : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);

    private static Vector4 Composite(Vector4 source, Vector4 backdrop, CompositeMode2D mode)
    {
        return mode switch
        {
            CompositeMode2D.Clear => Vector4.Zero,
            CompositeMode2D.Source => source,
            CompositeMode2D.Destination => backdrop,
            CompositeMode2D.SourceOver => source + (backdrop * (1 - source.W)),
            CompositeMode2D.DestinationOver => backdrop + (source * (1 - backdrop.W)),
            CompositeMode2D.SourceIn => source * backdrop.W,
            CompositeMode2D.DestinationIn => backdrop * source.W,
            CompositeMode2D.SourceOut => source * (1 - backdrop.W),
            CompositeMode2D.DestinationOut => backdrop * (1 - source.W),
            CompositeMode2D.SourceAtop => (source * backdrop.W) + (backdrop * (1 - source.W)),
            CompositeMode2D.DestinationAtop => (backdrop * source.W) + (source * (1 - backdrop.W)),
            CompositeMode2D.Xor => (source * (1 - backdrop.W)) + (backdrop * (1 - source.W)),
            CompositeMode2D.Plus => Vector4.Min(source + backdrop, Vector4.One),
            _ => throw new InvalidOperationException("The current-color fixtures must not require artistic blending in this reference evaluator."),
        };
    }

    private static Vector4 EvaluateLinear(LinearPaintNode2D linear, Vector2 point)
    {
        Vector2 perpendicular = linear.P2 - linear.P0;
        Vector2 normal = new(perpendicular.Y, -perpendicular.X);
        float denominator = Vector2.Dot(linear.P1 - linear.P0, normal);
        if (denominator == 0 || linear.Stops.Count == 0)
        {
            return Vector4.Zero;
        }

        float coordinate = Vector2.Dot(point - linear.P0, normal) / denominator;
        if (linear.Extend != ExtendMode2D.Pad)
        {
            throw new InvalidOperationException("The foreground fixture must use a padded gradient.");
        }

        ColorStop2D first = linear.Stops[0];
        if (coordinate <= first.Offset)
        {
            return Premultiply(first.Color);
        }

        for (int index = 1; index < linear.Stops.Count; index++)
        {
            ColorStop2D second = linear.Stops[index];
            if (coordinate < second.Offset)
            {
                float weight = (coordinate - first.Offset) / (second.Offset - first.Offset);
                return Vector4.Lerp(Premultiply(first.Color), Premultiply(second.Color), weight);
            }

            first = second;
        }

        return Premultiply(first.Color);
    }

    private static bool Contains(Path2D path, Vector2 point, FillRule2D rule)
    {
        Vector2 current = default;
        Vector2 start = default;
        int winding = 0;
        foreach (PathSegment2D segment in path.Segments)
        {
            switch (segment.Verb)
            {
                case PathVerb2D.Move:
                    start = current = segment.P0;
                    break;
                case PathVerb2D.Line:
                    Edge(segment.P0);
                    break;
                case PathVerb2D.Close:
                    Edge(start);
                    break;
                default:
                    throw new InvalidOperationException("The current-color fixtures must consist of line contours.");
            }
        }

        return rule == FillRule2D.NonZero ? winding != 0 : Math.Abs(winding) % 2 != 0;

        void Edge(Vector2 end)
        {
            float side = ((end.X - current.X) * (point.Y - current.Y)) - ((end.Y - current.Y) * (point.X - current.X));
            if (current.Y <= point.Y && end.Y > point.Y && side > 0)
            {
                winding++;
            }
            else if (current.Y > point.Y && end.Y <= point.Y && side < 0)
            {
                winding--;
            }

            current = end;
        }
    }
}
