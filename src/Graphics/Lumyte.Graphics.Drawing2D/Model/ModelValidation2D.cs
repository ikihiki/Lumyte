using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

internal static class ModelValidation2D
{
    internal static void Finite(float value, string name)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(name, "The value must be finite.");
        }
    }

    internal static void Unit(float value, string name)
    {
        Finite(value, name);
        if (value < 0 || value > 1)
        {
            throw new ArgumentOutOfRangeException(name, "The value must be between zero and one.");
        }
    }

    internal static void Nonnegative(float value, string name)
    {
        Finite(value, name);
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(name, "The value must be nonnegative.");
        }
    }

    internal static void Positive(float value, string name)
    {
        Finite(value, name);
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(name, "The value must be positive.");
        }
    }

    internal static void Vector(Vector2 value, string name)
    {
        Finite(value.X, name);
        Finite(value.Y, name);
    }

    internal static void Matrix(Matrix3x2 value, string name)
    {
        Finite(value.M11, name);
        Finite(value.M12, name);
        Finite(value.M21, name);
        Finite(value.M22, name);
        Finite(value.M31, name);
        Finite(value.M32, name);
    }

    internal static void Rectangle(Rect2D value, string name)
    {
        Finite(value.X, name);
        Finite(value.Y, name);
        Nonnegative(value.Width, name);
        Nonnegative(value.Height, name);
        Finite(value.X + value.Width, name);
        Finite(value.Y + value.Height, name);
    }

    internal static void Color(Color2D value, string name)
    {
        Unit(value.R, name);
        Unit(value.G, name);
        Unit(value.B, name);
        Unit(value.A, name);
    }

    internal static void EnumValue<T>(T value, string name)
        where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(name, "The enum value is not defined.");
        }
    }

    internal static void Stroke(StrokeStyle2D style, string name)
    {
        ArgumentNullException.ThrowIfNull(style, name);
        Positive(style.Width, name);
        EnumValue(style.Cap, name);
        EnumValue(style.Join, name);
        Finite(style.MiterLimit, name);
        if (style.MiterLimit < 1)
        {
            throw new ArgumentOutOfRangeException(name, "The miter limit must be at least one.");
        }
    }

    internal static void SampledView(IGraphicsTextureView view, string name)
    {
        ArgumentNullException.ThrowIfNull(view, name);
        if (view.Info.Dimension != TextureViewDimension.D2 || view.Info.MipLevelCount != 1 || view.Info.ArrayLayerCount != 1)
        {
            throw new NotSupportedException("Drawing images require a single-mip, single-layer D2 view.");
        }

        if ((view.Texture.Usage & TextureUsage.Sampled) == 0)
        {
            throw new ArgumentException("The image allocation requires Sampled usage.", name);
        }
    }

    internal static void Sampler(IGraphicsSampler sampler, string name)
    {
        ArgumentNullException.ThrowIfNull(sampler, name);
        if (sampler.Desc.Compare is not null)
        {
            throw new ArgumentException("Color and coverage images require a non-comparison sampler.", name);
        }
    }

    internal static void ColorView(IGraphicsTextureView view, string name)
    {
        SampledView(view, name);
        if (view.Info.Format is TextureFormat.Depth32Float or TextureFormat.Depth24Stencil8)
        {
            throw new NotSupportedException("Depth formats cannot be sampled as color images.");
        }
    }

    internal static void Source(Rect2D source, IGraphicsTextureView view, string name, bool integer = false)
    {
        Rectangle(source, name);
        (uint width, uint height) = view.Texture.GetMipSize(view.Info.BaseMipLevel);
        if (source.X < 0 || source.Y < 0 || (double)source.X + source.Width > width || (double)source.Y + source.Height > height)
        {
            throw new ArgumentOutOfRangeException(name, "The source must fit inside the selected image mip.");
        }

        if (integer && (source.Width <= 0 || source.Height <= 0 || source.X != MathF.Truncate(source.X) || source.Y != MathF.Truncate(source.Y) || source.Width != MathF.Truncate(source.Width) || source.Height != MathF.Truncate(source.Height)))
        {
            throw new ArgumentOutOfRangeException(name, "Pattern sources require integer coordinates and positive dimensions.");
        }
    }

    internal static Path2D RectanglePath(Rect2D rect)
    {
        var builder = new PathBuilder2D();
        builder.MoveTo(new Vector2(rect.X, rect.Y));
        builder.LineTo(new Vector2(rect.X + rect.Width, rect.Y));
        builder.LineTo(new Vector2(rect.X + rect.Width, rect.Y + rect.Height));
        builder.LineTo(new Vector2(rect.X, rect.Y + rect.Height));
        builder.Close();
        return builder.Build();
    }
}
