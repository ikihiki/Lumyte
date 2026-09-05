namespace Lumyte.Graphics.Text;

/// <summary>An immutable, callback-ordered COLRv1 paint program for one glyph.</summary>
internal sealed class ColorPaintGlyph
{
    internal ColorPaintGlyph(IEnumerable<ColorPaintOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);

        ColorPaintOperation[] copy = operations.ToArray();
        if (copy.Any(static operation => operation is null))
        {
            throw new ArgumentException("Paint operations cannot contain null values.", nameof(operations));
        }

        Operations = Array.AsReadOnly(copy);
    }

    internal IReadOnlyList<ColorPaintOperation> Operations { get; }
}
