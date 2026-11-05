using System.Numerics;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.UI.Media;

internal static class MediaGeometry
{
    internal static void Positive(float value, string name)
    {
        if (!float.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }

    internal static void Nonnegative(Vector2 value, string name)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || value.X < 0 || value.Y < 0)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }

    internal static void ViewBox(Rect2D box, string name)
    {
        Positive(box.Width, name);
        Positive(box.Height, name);
        if (!float.IsFinite(box.X) || !float.IsFinite(box.Y) || !float.IsFinite(box.X + box.Width) || !float.IsFinite(box.Y + box.Height))
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }

    internal static Vector2 Measure(Vector2 intrinsic, UiBoxConstraints constraints)
    {
        if (intrinsic.X > 0 && intrinsic.Y > 0)
        {
            if (constraints.MinWidth == constraints.MaxWidth && constraints.MinHeight != constraints.MaxHeight)
            {
                intrinsic = new(constraints.MinWidth, Finite((double)constraints.MinWidth * intrinsic.Y / intrinsic.X));
            }
            else if (constraints.MinHeight == constraints.MaxHeight && constraints.MinWidth != constraints.MaxWidth)
            {
                intrinsic = new(Finite((double)constraints.MinHeight * intrinsic.X / intrinsic.Y), constraints.MinHeight);
            }
        }

        if (intrinsic.X > 0 && intrinsic.Y > 0 && constraints.MinWidth != constraints.MaxWidth && constraints.MinHeight != constraints.MaxHeight)
        {
            double scale = Math.Min(1, Math.Min(constraints.MaxWidth / (double)intrinsic.X, constraints.MaxHeight / (double)intrinsic.Y));
            intrinsic = new(Finite(intrinsic.X * scale), Finite(intrinsic.Y * scale));
        }

        return constraints.Constrain(intrinsic);
    }

    internal static Rect2D Place(Vector2 intrinsic, Vector2 body, ImageStretch stretch)
    {
        double width = intrinsic.X;
        double height = intrinsic.Y;
        if (stretch == ImageStretch.Fill)
        {
            width = body.X;
            height = body.Y;
        }
        else if (stretch != ImageStretch.None)
        {
            double x = body.X / width;
            double y = body.Y / height;
            double scale = stretch == ImageStretch.Uniform ? Math.Min(x, y) : Math.Max(x, y);
            width *= scale;
            height *= scale;
        }

        return new(Finite((body.X - width) / 2), Finite((body.Y - height) / 2), Finite(width), Finite(height));
    }

    private static float Finite(double value) => float.IsFinite((float)value) ? (float)value : throw new InvalidOperationException("Media geometry exceeds finite logical coordinates.");
}
