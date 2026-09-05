using System.Collections.ObjectModel;
using System.Numerics;

namespace Lumyte.Graphics.TwoD;

public readonly record struct Brush
{
    private static readonly IReadOnlyList<GradientStop> NoGradientStops = Array.Empty<GradientStop>();
    private readonly ReadOnlyCollection<GradientStop>? gradientStops;

    public Brush(Color color)
    {
        Kind = BrushKind.Solid;
        Color = color.Validate();
        SecondaryColor = Color;
    }

    private Brush(
        BrushKind kind,
        Color color,
        Color secondaryColor,
        Vector2 start,
        Vector2 end,
        Vector2 point0,
        Vector2 point1,
        Vector2 point2,
        float radius0,
        float radius1,
        float startAngle,
        float endAngle,
        GradientExtendMode extendMode,
        GradientStop[] stops)
    {
        Kind = kind;
        Color = color;
        SecondaryColor = secondaryColor;
        Start = start;
        End = end;
        Point0 = point0;
        Point1 = point1;
        Point2 = point2;
        Radius0 = radius0;
        Radius1 = radius1;
        StartAngle = startAngle;
        EndAngle = endAngle;
        ExtendMode = extendMode;
        gradientStops = Array.AsReadOnly(stops);
    }

    public BrushKind Kind { get; }
    public Color Color { get; }
    public Color SecondaryColor { get; }
    public Vector2 Start { get; }
    public Vector2 End { get; }
    /// <summary>The complete, offset-ordered color line for a gradient brush.</summary>
    public IReadOnlyList<GradientStop> GradientStops => gradientStops ?? NoGradientStops;
    public GradientExtendMode ExtendMode { get; }
    /// <summary>The first linear anchor, first radial center, or sweep center.</summary>
    public Vector2 Point0 { get; }
    /// <summary>The second linear anchor or second radial center.</summary>
    public Vector2 Point1 { get; }
    /// <summary>The projection-direction anchor of a COLRv1 linear gradient.</summary>
    public Vector2 Point2 { get; }
    public float Radius0 { get; }
    public float Radius1 { get; }
    public float StartAngle { get; }
    public float EndAngle { get; }

    internal bool UsesLegacyGpuLayout => Kind switch
    {
        BrushKind.Solid => true,
        BrushKind.LinearGradient => ExtendMode == GradientExtendMode.Pad
            && HasLegacyStops()
            && Point0 == Start
            && Point1 == End
            && Point2 == Start + new Vector2(-(End.Y - Start.Y), End.X - Start.X),
        BrushKind.RadialGradient => ExtendMode == GradientExtendMode.Pad
            && HasLegacyStops()
            && Point0 == Point1
            && Radius0 == 0
            && Radius1 == End.X
            && Point0 == Start,
        _ => false,
    };

    public static Brush Solid(Color color) => new(color);

    public static Brush LinearGradient(
        Vector2 start,
        Vector2 end,
        Color startColor,
        Color endColor)
    {
        ValidatePoint(start, nameof(start));
        ValidatePoint(end, nameof(end));
        if (Vector2.DistanceSquared(start, end) <= float.Epsilon)
        {
            throw new ArgumentException("Linear-gradient points must differ.", nameof(end));
        }
        return new(
            BrushKind.LinearGradient,
            startColor.Validate(),
            endColor.Validate(),
            start,
            end,
            start,
            end,
            start + new Vector2(-(end.Y - start.Y), end.X - start.X),
            0,
            0,
            0,
            0,
            GradientExtendMode.Pad,
            [new(0, startColor), new(1, endColor)]);
    }

    /// <summary>Creates a COLRv1-compatible three-anchor linear gradient.</summary>
    public static Brush LinearGradient(
        Vector2 point0,
        Vector2 point1,
        Vector2 point2,
        ReadOnlySpan<GradientStop> stops,
        GradientExtendMode extendMode = GradientExtendMode.Pad)
    {
        ValidatePoint(point0, nameof(point0));
        ValidatePoint(point1, nameof(point1));
        ValidatePoint(point2, nameof(point2));
        if (Vector2.DistanceSquared(point0, point1) <= float.Epsilon)
        {
            throw new ArgumentException("Linear-gradient color-line anchors must differ.", nameof(point1));
        }
        if (Vector2.DistanceSquared(point0, point2) <= float.Epsilon)
        {
            throw new ArgumentException("Linear-gradient projection anchors must differ.", nameof(point2));
        }
        ValidateExtendMode(extendMode, nameof(extendMode));
        GradientStop[] copied = CopyStops(stops, nameof(stops));
        return new(
            BrushKind.LinearGradient,
            copied[0].Color,
            copied[^1].Color,
            point0,
            point1,
            point0,
            point1,
            point2,
            0,
            0,
            0,
            0,
            extendMode,
            copied);
    }

    public static Brush RadialGradient(
        Vector2 center,
        float radius,
        Color innerColor,
        Color outerColor)
    {
        ValidatePoint(center, nameof(center));
        if (!float.IsFinite(radius) || radius <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(radius));
        }
        return new(
            BrushKind.RadialGradient,
            innerColor.Validate(),
            outerColor.Validate(),
            center,
            new(radius, 0),
            center,
            center,
            default,
            0,
            radius,
            0,
            0,
            GradientExtendMode.Pad,
            [new(0, innerColor), new(1, outerColor)]);
    }

    /// <summary>
    /// Creates a COLRv1-compatible radial gradient between two circles. Radii may be negative
    /// because OpenType variation deltas can move them below zero.
    /// </summary>
    public static Brush RadialGradient(
        Vector2 center0,
        float radius0,
        Vector2 center1,
        float radius1,
        ReadOnlySpan<GradientStop> stops,
        GradientExtendMode extendMode = GradientExtendMode.Pad)
    {
        ValidatePoint(center0, nameof(center0));
        ValidatePoint(center1, nameof(center1));
        ValidateFinite(radius0, nameof(radius0));
        ValidateFinite(radius1, nameof(radius1));
        ValidateExtendMode(extendMode, nameof(extendMode));
        GradientStop[] copied = CopyStops(stops, nameof(stops));
        return new(
            BrushKind.RadialGradient,
            copied[0].Color,
            copied[^1].Color,
            center0,
            center1,
            center0,
            center1,
            default,
            radius0,
            radius1,
            0,
            0,
            extendMode,
            copied);
    }

    /// <summary>
    /// Creates a COLRv1-compatible angular sweep gradient whose angles are in radians.
    /// In the library's y-down coordinate space, positive COLRv1 counter-clockwise design-space
    /// angles are represented by negative values. The sampled revolution is therefore (-2π, 0].
    /// </summary>
    public static Brush SweepGradient(
        Vector2 center,
        float startAngle,
        float endAngle,
        ReadOnlySpan<GradientStop> stops,
        GradientExtendMode extendMode = GradientExtendMode.Pad)
    {
        ValidatePoint(center, nameof(center));
        ValidateFinite(startAngle, nameof(startAngle));
        ValidateFinite(endAngle, nameof(endAngle));
        ValidateExtendMode(extendMode, nameof(extendMode));
        GradientStop[] copied = CopyStops(stops, nameof(stops));
        return new(
            BrushKind.SweepGradient,
            copied[0].Color,
            copied[^1].Color,
            center,
            new(startAngle, endAngle),
            center,
            default,
            default,
            0,
            0,
            startAngle,
            endAngle,
            extendMode,
            copied);
    }

    public Brush Validate()
    {
        if (!Enum.IsDefined(Kind)) { throw new ArgumentOutOfRangeException(nameof(Kind)); }
        Color.Validate();
        SecondaryColor.Validate();
        ValidatePoint(Start, nameof(Start));
        ValidatePoint(End, nameof(End));
        if (!Enum.IsDefined(ExtendMode)) { throw new ArgumentOutOfRangeException(nameof(ExtendMode)); }
        if (Kind == BrushKind.LinearGradient && Vector2.DistanceSquared(Start, End) <= float.Epsilon)
        {
            throw new ArgumentException("Linear-gradient points must differ.", nameof(End));
        }
        if (Kind == BrushKind.RadialGradient && End.X <= 0)
        {
            if (UsesLegacyGpuLayout)
            {
                throw new ArgumentException("Radial-gradient radius must be positive.", nameof(End));
            }
        }
        if (Kind != BrushKind.Solid)
        {
            if (gradientStops is null || gradientStops.Count == 0)
            {
                throw new ArgumentException("Gradient brushes require at least one color stop.");
            }
            foreach (GradientStop stop in gradientStops)
            {
                stop.Validate(nameof(GradientStops));
            }
        }
        return this;
    }

    public bool Equals(Brush other)
    {
        if (Kind != other.Kind
            || Color != other.Color
            || SecondaryColor != other.SecondaryColor
            || Start != other.Start
            || End != other.End
            || ExtendMode != other.ExtendMode
            || Point0 != other.Point0
            || Point1 != other.Point1
            || Point2 != other.Point2
            || Radius0 != other.Radius0
            || Radius1 != other.Radius1
            || StartAngle != other.StartAngle
            || EndAngle != other.EndAngle
            || GradientStops.Count != other.GradientStops.Count)
        {
            return false;
        }
        for (int index = 0; index < GradientStops.Count; index++)
        {
            if (GradientStops[index] != other.GradientStops[index])
            {
                return false;
            }
        }
        return true;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(Color);
        hash.Add(SecondaryColor);
        hash.Add(Start);
        hash.Add(End);
        hash.Add(ExtendMode);
        hash.Add(Point0);
        hash.Add(Point1);
        hash.Add(Point2);
        hash.Add(Radius0);
        hash.Add(Radius1);
        hash.Add(StartAngle);
        hash.Add(EndAngle);
        foreach (GradientStop stop in GradientStops)
        {
            hash.Add(stop);
        }
        return hash.ToHashCode();
    }

    private bool HasLegacyStops()
        => GradientStops.Count == 2
            && GradientStops[0].Offset == 0
            && GradientStops[1].Offset == 1;

    private static GradientStop[] CopyStops(ReadOnlySpan<GradientStop> stops, string parameterName)
    {
        if (stops.IsEmpty)
        {
            throw new ArgumentException("A gradient requires at least one color stop.", parameterName);
        }
        GradientStop[] copied = stops.ToArray();
        for (int index = 0; index < copied.Length; index++)
        {
            copied[index].Validate(parameterName);
        }
        for (int index = 1; index < copied.Length; index++)
        {
            GradientStop current = copied[index];
            int insertion = index;
            while (insertion > 0 && copied[insertion - 1].Offset > current.Offset)
            {
                copied[insertion] = copied[insertion - 1];
                insertion--;
            }
            copied[insertion] = current;
        }
        return copied;
    }

    private static void ValidateExtendMode(GradientExtendMode mode, string parameterName)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void ValidateFinite(float value, string parameterName)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void ValidatePoint(Vector2 point, string parameter)
    {
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y))
        {
            throw new ArgumentOutOfRangeException(parameter);
        }
    }
}
