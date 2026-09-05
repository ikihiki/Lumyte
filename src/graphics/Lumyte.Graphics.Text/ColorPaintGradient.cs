namespace Lumyte.Graphics.Text;

/// <summary>An immutable set of COLRv1 gradient stops and its extension rule.</summary>
internal sealed class ColorPaintGradient
{
    internal ColorPaintGradient(
        IEnumerable<ColorPaintGradientStop> stops,
        ColorPaintExtendMode extendMode)
    {
        ArgumentNullException.ThrowIfNull(stops);

        ColorPaintGradientStop[] copy = stops.ToArray();
        Stops = Array.AsReadOnly(copy);
        ExtendMode = extendMode;
    }

    internal IReadOnlyList<ColorPaintGradientStop> Stops { get; }
    internal ColorPaintExtendMode ExtendMode { get; }
}
