namespace Lumyte.Graphics.Drawing2D;

/// <summary>An immutable intersection of geometric clips with individually captured transforms.</summary>
public sealed class ClipStack2D
{
    internal ClipStack2D(IEnumerable<ClipElement2D> elements)
    {
        Elements = Array.AsReadOnly(elements.ToArray());
    }

    /// <summary>Gets the immutable clip element snapshot.</summary>
    public IReadOnlyList<ClipElement2D> Elements { get; }

    internal static ClipStack2D Empty { get; } = new(Array.Empty<ClipElement2D>());

    internal ClipStack2D Add(ClipElement2D element) => new(Elements.Append(element));
}
