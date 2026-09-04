using System.Numerics;

namespace Lumyte.Graphics.TwoD;

/// <summary>Immutable drawable content stored by one retained scene node.</summary>
public readonly struct SceneContent
{
    private SceneContent(RecordedCommand command, SceneContentKind kind)
    {
        Command = command;
        Kind = kind;
    }

    public SceneContentKind Kind { get; }

    internal RecordedCommand Command { get; }

    public static SceneContent Rectangle(Rect rectangle, Brush brush)
        => new(new(DrawCommandKind.Rectangle, rectangle.Validate(), brush.Validate(), default, null), SceneContentKind.Rectangle);

    public static SceneContent RoundedRectangle(Rect rectangle, CornerRadius radius, Brush brush)
        => new(new(
            DrawCommandKind.RoundedRectangle,
            rectangle.Validate(),
            brush.Validate(),
            default,
            null,
            CornerRadius: radius.Validate().Clamp(rectangle)), SceneContentKind.RoundedRectangle);

    public static SceneContent Ellipse(Rect bounds, Brush brush)
        => new(new(DrawCommandKind.Ellipse, bounds.Validate(), brush.Validate(), default, null), SceneContentKind.Ellipse);

    public static SceneContent Line(Vector2 start, Vector2 end, float width, Brush brush)
    {
        Validate(start, nameof(start));
        Validate(end, nameof(end));
        if (!float.IsFinite(width) || width <= 0) { throw new ArgumentOutOfRangeException(nameof(width)); }
        float halfWidth = width * 0.5f;
        var bounds = new Rect(
            MathF.Min(start.X, end.X) - halfWidth,
            MathF.Min(start.Y, end.Y) - halfWidth,
            MathF.Max(MathF.Abs(end.X - start.X) + width, width),
            MathF.Max(MathF.Abs(end.Y - start.Y) + width, width));
        return new(new(
            DrawCommandKind.Line,
            bounds,
            brush.Validate(),
            default,
            null,
            LineStart: start,
            LineEnd: end,
            LineWidth: width), SceneContentKind.Line);
    }

    public static SceneContent Image(ImageId image, Rect destination, Color? tint = null, Rect? source = null)
    {
        if (image.IsNull) { throw new ArgumentException("Image cannot be null.", nameof(image)); }
        Rect sourceRectangle = (source ?? new(0, 0, 1, 1)).Validate();
        return new(new(
            DrawCommandKind.Image,
            destination.Validate(),
            Brush.Solid(tint ?? Color.White),
            default,
            null,
            Image: image,
            Source: sourceRectangle), SceneContentKind.Image);
    }

    public static SceneContent DistanceField(DistanceField field, Rect destination, Brush brush)
    {
        DistanceFieldAtlas atlas = field.Owner
            ?? throw new ArgumentException("Distance field cannot be null.", nameof(field));
        DistanceFieldEntry entry = atlas.Require(field);
        GpuTextureDescription description = atlas.Description;
        Rect source = new(
            (float)entry.Region.X / description.Width,
            (float)entry.Region.Y / description.Height,
            (float)entry.Region.Width / description.Width,
            (float)entry.Region.Height / description.Height);
        return new(new(
            DrawCommandKind.DistanceField,
            destination.Validate(),
            brush.Validate(),
            default,
            null,
            Source: source,
            DistanceField: field), SceneContentKind.DistanceField);
    }

    internal RecordedCommand Resolve(Renderer renderer, Matrix3x2 transform, Rect? clip)
    {
        RecordedCommand command = Command with { Transform = transform, Clip = clip };
        if (command.Kind == DrawCommandKind.Image)
        {
            renderer.RequireImage(command.Image);
        }
        else if (command.Kind == DrawCommandKind.DistanceField)
        {
            command = command with { Image = renderer.RequireDistanceField(command.DistanceField) };
        }
        return command;
    }

    private static void Validate(Vector2 point, string parameter)
    {
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y))
        {
            throw new ArgumentOutOfRangeException(parameter);
        }
    }
}
