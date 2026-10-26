using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

internal sealed class DrawingProgram2D
{
    internal const int MaximumInstructions = 256;
    private readonly IArgumentTable _table;
    private readonly IGpuRef<IGraphicsSampler> _nearest;
    private readonly Matrix3x2 _pixelTransform;
    private uint _textureSlot;

    internal DrawingProgram2D(IArgumentTable table, IGpuRef<IGraphicsSampler> nearest, DrawingTargetInfo2D target)
    {
        _table = table;
        _nearest = nearest;
        _pixelTransform = Matrix3x2.CreateScale(target.PixelScale) * Matrix3x2.CreateTranslation(-target.PixelOrigin);
    }

    internal List<DrawingInstruction> Instructions { get; } = [];

    internal List<Vector4> Data { get; } = [];

    internal HashSet<IGraphicsTexture> Textures { get; } = new(ReferenceEqualityComparer.Instance);

    internal static Vector4 Linear(Color2D color)
    {
        static float Decode(float c) => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
        return new(Decode(color.R) * color.A, Decode(color.G) * color.A, Decode(color.B) * color.A, color.A);
    }

    internal uint Compile(DrawingCommand2D command)
    {
        Matrix3x2 transform = command.State.Transform * _pixelTransform;
        if (!Invertible(transform))
        {
            return Solid(default);
        }

        uint shape;
        uint paint;
        switch (command)
        {
            case FillRectCommand2D c:
                shape = Rect(c.Rect, transform);
                paint = Paint(c.Paint.Root, transform);
                break;
            case FillRoundedRectCommand2D c:
                shape = Add(Node(2, transform) with { A = Vector(c.Rect), B = new(MathF.Min(c.Radius, MathF.Min(c.Rect.Width, c.Rect.Height) * 0.5f), 0, 0, 0) });
                paint = Paint(c.Paint.Root, transform);
                break;
            case FillCircleCommand2D c:
                shape = Add(Node(3, transform) with { A = new(c.Center, c.Radius, 0) });
                paint = Paint(c.Paint.Root, transform);
                break;
            default:
                throw new ArgumentException("Unknown drawing command.", nameof(command));
        }

        uint result = Multiply(shape, paint);
        return ApplyClip(result, command.State);
    }

    internal uint CompileClip(DrawingState2D state) => ApplyClip(Solid(Vector4.One), state);

    internal void ValidateFiniteData()
    {
        foreach (DrawingInstruction instruction in Instructions)
        {
            Check(instruction.T0);
            Check(instruction.T1);
            Check(instruction.A);
            Check(instruction.B);
            Check(instruction.C);
            Check(instruction.D);
        }

        foreach (Vector4 vector in Data)
        {
            Check(vector);
        }

        static void Check(Vector4 vector)
        {
            if (!float.IsFinite(vector.X) || !float.IsFinite(vector.Y) || !float.IsFinite(vector.Z) || !float.IsFinite(vector.W))
            {
                throw new NotSupportedException("Drawing geometry exceeds the finite shader coordinate range.");
            }
        }
    }

    private static Vector4 Vector(Rect2D rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    private static DrawingInstruction Node(uint kind, Matrix3x2 transform)
    {
        if (!Invertible(transform) || !Matrix3x2.Invert(transform, out Matrix3x2 inverse))
        {
            return new(default, default, default, default, default, default, 0, 0, 0, 0, null!, null!);
        }

        return new(new(inverse.M11, inverse.M21, inverse.M31, 0), new(inverse.M12, inverse.M22, inverse.M32, 0), default, default, default, default, kind, 0, 0, 0, null!, null!);
    }

    private static bool Invertible(Matrix3x2 transform) => Matrix3x2.Invert(transform, out Matrix3x2 inverse) && float.IsFinite(inverse.M11) && float.IsFinite(inverse.M12) && float.IsFinite(inverse.M21) && float.IsFinite(inverse.M22) && float.IsFinite(inverse.M31) && float.IsFinite(inverse.M32);

    private uint ApplyClip(uint result, DrawingState2D state)
    {
        foreach (ClipElement2D clip in state.Clip.Elements)
        {
            result = Multiply(result, Path(clip.Path, clip.Rule, clip.Transform * _pixelTransform));
        }

        return result;
    }

    private uint Paint(PaintNode2D paint, Matrix3x2 transform, int depth = 0)
    {
        if (depth > 64)
        {
            throw new NotSupportedException("Paint graph exceeds the 64 level evaluation budget.");
        }

        if (!Invertible(transform))
        {
            return Solid(default);
        }

        switch (paint)
        {
            case SolidPaintNode2D node:
                return Solid(Linear(node.Color));
            case LinearPaintNode2D node:
                Vector2 delta = node.P2 - node.P0;
                if (Vector2.Dot(node.P1 - node.P0, new(delta.Y, -delta.X)) == 0)
                {
                    throw new NotSupportedException("Degenerate linear gradients are not supported by the default renderer.");
                }

                return Gradient(Node(6, transform) with { A = new(node.P0, node.P1.X, node.P1.Y), B = new(node.P2, 0, 0), Mode = (uint)node.Extend }, node.Stops);
            case RadialPaintNode2D node:
                if (node.C0 == node.C1 && node.R0 == node.R1)
                {
                    throw new NotSupportedException("Coincident identical radial circles are not supported by the default renderer.");
                }

                return Gradient(Node(7, transform) with { A = new(node.C0, node.R0, 0), B = new(node.C1, node.R1, 0), Mode = (uint)node.Extend }, node.Stops);
            case SweepPaintNode2D node:
                if (node.StartAngle == node.EndAngle)
                {
                    throw new NotSupportedException("Zero-angle sweep gradients are not supported by the default renderer.");
                }

                return Gradient(Node(8, transform) with { A = new(node.Center, node.StartAngle, node.EndAngle), Mode = (uint)node.Extend }, node.Stops);
            case ImagePaintNode2D node:
                ImagePattern2D p = node.Pattern;
                return Image(p.View, p.Source, p.Alpha, p.TileX, p.TileY, p.Filter, p.PatternToLocal * transform);
            case OpacityPaintNode2D node:
                return Add(Node(10, Matrix3x2.Identity) with { First = Paint(node.Child, transform, depth + 1), A = new(node.Opacity) });
            case ClipPaintNode2D node:
                return Multiply(Path(node.Path, node.Rule, transform), Paint(node.Child, transform, depth + 1));
            case TransformPaintNode2D node:
                return Paint(node.Child, node.Transform * transform, depth + 1);
            default:
                throw new ArgumentException("Unknown paint node.", nameof(paint));
        }
    }

    private uint Path(Path2D path, FillRule2D rule, Matrix3x2 transform)
    {
        uint first = (uint)Data.Count;
        foreach (PathFlattening2D.Contour contour in PathFlattening2D.Flatten(path, transform))
        {
            for (int i = 0; i < contour.Points.Count; i++)
            {
                Vector2 a = contour.Points[i];
                Vector2 b = contour.Points[(i + 1) % contour.Points.Count];
                if (a != b)
                {
                    Data.Add(new(a.X, a.Y, b.X, b.Y));
                }
            }
        }

        return Add(Node(4, transform) with { First = first, Count = (uint)Data.Count - first, Mode = (uint)rule });
    }

    private uint Rect(Rect2D rect, Matrix3x2 transform) => Add(Node(1, transform) with { A = Vector(rect) });

    private uint Solid(Vector4 color) => Add(Node(0, Matrix3x2.Identity) with { A = color });

    private uint Multiply(uint a, uint b) => Add(Node(11, Matrix3x2.Identity) with { First = a, Count = b });

    private uint Gradient(DrawingInstruction instruction, IReadOnlyList<ColorStop2D> stops)
    {
        if (stops.Count > 0 && instruction.Mode != 0 && stops[0].Offset == stops[^1].Offset)
        {
            throw new NotSupportedException("Repeating zero-span color lines are not supported by the default renderer.");
        }

        uint first = (uint)Data.Count;
        foreach (ColorStop2D stop in stops)
        {
            Data.Add(new(stop.Offset, 0, 0, 0));
            Data.Add(Linear(stop.Color));
        }

        return Add(instruction with { First = first, Count = (uint)stops.Count });
    }

    private uint Image(IGraphicsTextureView view, Rect2D source, AlphaRepresentation2D alpha, ImageTileMode2D tileX, ImageTileMode2D tileY, ImageFilter2D filter, Matrix3x2 transform)
    {
        (uint width, uint height) = view.Texture.GetMipSize(view.Info.BaseMipLevel);
        return Add(Node(9, transform) with
        {
            A = Vector(source),
            B = new(width, height, (float)tileX, (float)tileY),
            C = new((float)filter, (float)alpha, 0, 0),
            Texture = Register(view),
            Sampler = _nearest,
        });
    }

    private IGpuRef<IGraphicsTextureView> Register(IGraphicsTextureView view)
    {
        if (_textureSlot >= MaximumInstructions)
        {
            throw new NotSupportedException("Paint graph exceeds its texture binding budget.");
        }

        Textures.Add(view.Texture);
        return _table.WriteTexture(_textureSlot++, view);
    }

    private uint Add(DrawingInstruction instruction)
    {
        if (Instructions.Count >= MaximumInstructions || Data.Count > 262144)
        {
            throw new NotSupportedException("Drawing exceeds the 256 instruction or 262144 geometry element budget.");
        }

        uint index = (uint)Instructions.Count;
        Instructions.Add(instruction);
        return index;
    }
}
