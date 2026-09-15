using System.Numerics;

using Lumyte.Graphics.Text;
using Lumyte.Graphics.TwoD;

namespace Lumyte.Graphics.Native.Passes;

internal sealed record NativePreparedDraw(Vector4[] Data, Draw2DImageSource? Image = null, Draw2DImageSource? Mask = null);

internal static class NativeDrawPreparation
{
    internal static NativePreparedDraw Prepare(Draw2DCommand command, Matrix3x2 transform, IReadOnlyList<Draw2DClip> clips,
        CompositeMode composite = CompositeMode.SourceOver, float opacity = 1)
    {
        List<Vector4> data = Enumerable.Repeat(Vector4.Zero, 20).ToList();
        bool invertible = Matrix3x2.Invert(transform, out Matrix3x2 inverse);
        if (!invertible)
        { inverse = Matrix3x2.Identity; }
        data[0] = new(inverse.M11, inverse.M12, inverse.M21, inverse.M22);
        data[1] = new(inverse.M31, inverse.M32, 0, 0);
        data[9] = new(0, 0, 0, (float)composite);
        data[12] = Vector4.One;
        data[14] = new(0, 0, opacity, 0);
        Brush brush;
        Draw2DImageSource? image = null;
        float scale = MathF.Max(new Vector2(transform.M11, transform.M12).Length(), new Vector2(transform.M21, transform.M22).Length());
        float tolerance = 0.0625f / MathF.Max(scale, 1e-5f);
        switch (command)
        {
            case Draw2DShapeCommand shape:
                data[1] = data[1] with { W = (float)shape.Kind };
                data[2] = RectVector(shape.Bounds);
                float radius = MathF.Min(shape.Bounds.Width, shape.Bounds.Height) * .5f;
                data[3] = new(MathF.Min(radius, shape.CornerRadius.TopLeft), MathF.Min(radius, shape.CornerRadius.TopRight),
                    MathF.Min(radius, shape.CornerRadius.BottomRight), MathF.Min(radius, shape.CornerRadius.BottomLeft));
                brush = shape.Brush;
                break;
            case Draw2DPathCommand path:
                int start = data.Count;
                var contours = NativePathPreparation.Flatten(path.Path, tolerance);
                if (path.Stroke is { } stroke)
                { NativePathPreparation.StrokeEdges(data, contours, stroke, tolerance); }
                else
                { NativePathPreparation.FillEdges(data, contours, Matrix3x2.Identity); }
                data[1] = data[1] with { W = 3 };
                data[8] = new(start, data.Count - start, path.Stroke is null && path.FillRule == FillRule.EvenOdd ? 1 : 0, 0);
                brush = path.Brush;
                break;
            case Draw2DGeometryCommand geometry:
                int geometryStart = data.Count;
                for (int i = 0; i < geometry.Geometry.Vertices.Count; i += 3)
                { NativePathPreparation.Polygon(data, [geometry.Geometry.Vertices[i], geometry.Geometry.Vertices[i + 1], geometry.Geometry.Vertices[i + 2]]); }
                data[1] = data[1] with { W = 3 };
                data[8] = new(geometryStart, data.Count - geometryStart, 0, 0);
                brush = geometry.Brush;
                break;
            case Draw2DImageCommand drawImage:
                Image(drawImage.Source, drawImage.Destination, drawImage.SourceRectangle, drawImage.Sampling);
                data[12] = drawImage.Tint.Premultiplied();
                brush = Brush.Solid(Color.White);
                break;
            case Draw2DDistanceFieldCommand distance:
                Image(new(distance.Data.Image), distance.Destination, new(0, 0, distance.Data.Image.Description.Width, distance.Data.Image.Description.Height), default);
                data[13] = data[13] with { Z = (int)distance.Data.Kind + 1 };
                data[14] = data[14] with { X = distance.Data.DistanceRange * scale * MathF.Min(distance.Destination.Width / distance.Data.SourceSize.X, distance.Destination.Height / distance.Data.SourceSize.Y) };
                brush = distance.Brush;
                break;
            default:
                throw new ArgumentException("Only draw leaf commands have Native draw data.", nameof(command));
        }

        switch (brush)
        {
            case SolidBrush solid:
                data[4] = solid.Color.Premultiplied();
                break;
            case LinearGradientBrush linear:
                Vector2 x = linear.End - linear.Start, y = linear.Projection - linear.Start;
                float determinant = x.X * y.Y - x.Y * y.X;
                Vector2 projection = MathF.Abs(determinant) > 1e-12f ? new(y.Y / determinant, -y.X / determinant) : x / x.LengthSquared();
                data[5] = new(linear.Start.X, linear.Start.Y, projection.X, projection.Y);
                Gradient(linear, 1);
                break;
            case RadialGradientBrush radial:
                data[5] = new(radial.Center0.X, radial.Center0.Y, radial.Center1.X, radial.Center1.Y);
                data[6] = new(radial.Radius0, radial.Radius1, 0, 0);
                Gradient(radial, 2);
                break;
            case SweepGradientBrush sweep:
                data[5] = new(sweep.Center.X, sweep.Center.Y, 0, 0);
                data[6] = new(sweep.StartAngle, sweep.EndAngle, 0, 0);
                Gradient(sweep, 3);
                break;
            case ImageBrush imageBrush:
                if (!Matrix3x2.Invert(imageBrush.Transform, out Matrix3x2 imageInverse))
                { image = null; data[9] = data[9] with { Z = 0 }; data[4] = Vector4.Zero; break; }
                image = imageBrush.Source;
                data[9] = data[9] with { Z = 1 };
                data[15] = new(imageInverse.M11, imageInverse.M12, imageInverse.M21, imageInverse.M22);
                data[16] = new(imageInverse.M31, imageInverse.M32, 1, 0);
                data[13] = new(imageBrush.Sampling.Filter == Draw2DImageFilter.Linear ? 1 : 0,
                    (int)imageBrush.Sampling.ExtendX, 0, 0);
                data[17] = data[17] with { X = (int)imageBrush.Sampling.ExtendY };
                break;
        }
        int clipStart = data.Count;
        foreach (var unused in clips)
        { data.Add(default); }
        for (int i = 0; i < clips.Count; i++)
        {
            var clip = clips[i];
            int edgeStart = data.Count;
            if (clip.Rectangle is { } rectangle)
            { NativePathPreparation.Polygon(data, NativePathPreparation.Rectangle(rectangle).Select(p => Vector2.Transform(p, clip.Transform)).ToArray()); }
            else if (clip.Path is { } path)
            { NativePathPreparation.FillEdges(data, NativePathPreparation.Flatten(path, 0.0625f / MathF.Max(MaxScale(clip.Transform), 1e-5f)), clip.Transform); }
            data[clipStart + i] = new(edgeStart, data.Count - edgeStart, clip.FillRule == FillRule.EvenOdd ? 1 : 0, 0);
        }
        data[9] = data[9] with { X = clipStart, Y = clips.Count };
        if (!invertible || MathF.Abs(transform.GetDeterminant()) < 1e-20f)
        { data[1] = data[1] with { W = 0 }; data[2] = Vector4.Zero; }
        return new(data.ToArray(), image);

        void Image(Draw2DImageSource source, Rect destination, Rect sourceRect, Draw2DImageSampling sampling)
        {
            image = source;
            data[2] = RectVector(destination);
            data[10] = RectVector(sourceRect);
            data[11] = RectVector(destination);
            data[9] = data[9] with { Z = 1 };
            data[13] = new(sampling.Filter == Draw2DImageFilter.Linear ? 1 : 0, (int)sampling.ExtendX, 0, 0);
            data[17] = data[17] with { X = (int)sampling.ExtendY };
        }
        void Gradient(GradientBrush gradient, int kind)
        {
            data[7] = new(kind, (int)gradient.ExtendMode, gradient.Stops.Count, data.Count);
            foreach (GradientStop stop in gradient.Stops)
            { data.Add(new(stop.Offset, 0, 0, 0)); data.Add(stop.Color.Premultiplied()); }
        }
    }
    internal static Vector4 RectVector(Rect rectangle) => new(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
    internal static float MaxScale(Matrix3x2 transform) => MathF.Max(new Vector2(transform.M11, transform.M12).Length(), new Vector2(transform.M21, transform.M22).Length());
}
