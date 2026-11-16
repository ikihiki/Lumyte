using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Text;

namespace Lumyte.UI.Media;

/// <summary>An immutable vector, shaped glyph or borrowed image icon with explicit source coordinates.</summary>
public sealed class IconSource
{
    private readonly Path2D? _path;
    private readonly PaintGraph2D? _paint;
    private readonly TextLayout2D? _glyph;
    private readonly PreparedImage? _image;

    private IconSource(Rect2D viewBox, Path2D? path = null, PaintGraph2D? paint = null, TextLayout2D? glyph = null, PreparedImage? image = null)
    {
        MediaGeometry.ViewBox(viewBox, nameof(viewBox));
        ViewBox = viewBox;
        _path = path;
        _paint = paint;
        _glyph = glyph;
        _image = image;
    }

    /// <summary>Gets the finite positive source coordinate rectangle.</summary>
    public Rect2D ViewBox { get; }

    /// <summary>Creates an immutable vector representation.</summary>
    /// <param name="path">The immutable path.</param>
    /// <param name="viewBox">Explicit logical source coordinates.</param>
    /// <param name="paint">The immutable paint, borrowing any external GPU dependencies.</param>
    /// <returns>The prepared representation.</returns>
    public static IconSource Vector(Path2D path, Rect2D viewBox, PaintGraph2D paint)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(paint);
        return new(viewBox, path, paint);
    }

    /// <summary>Creates a shaped glyph representation without font ownership.</summary>
    /// <param name="layout">An independent immutable shaped snapshot.</param>
    /// <param name="viewBox">Explicit source coordinates, including the desired advance or ink region.</param>
    /// <returns>The prepared representation.</returns>
    public static IconSource Glyph(TextLayout2D layout, Rect2D viewBox)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return new(viewBox, glyph: layout);
    }

    /// <summary>Creates a borrowed image representation; its owner must outlive the final GPU consumer.</summary>
    /// <param name="image">The borrowed prepared image.</param>
    /// <returns>The prepared representation.</returns>
    public static IconSource Image(PreparedImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return new(new(0, 0, image.IntrinsicSize.X, image.IntrinsicSize.Y), image: image);
    }

    internal void Record(DrawingCanvas2D canvas, Vector2 body)
    {
        if (body.X <= 0 || body.Y <= 0)
        {
            return;
        }

        Rect2D destination = MediaGeometry.Place(new(ViewBox.Width, ViewBox.Height), body, ImageStretch.Uniform);
        canvas.Save();
        try
        {
            canvas.Translate(new Vector2(destination.X, destination.Y));
            canvas.Scale(new Vector2(destination.Width / ViewBox.Width, destination.Height / ViewBox.Height));
            canvas.Translate(new Vector2(-ViewBox.X, -ViewBox.Y));
            if (_path is not null)
            {
                canvas.FillPath(_path, _paint!);
            }
            else if (_glyph is not null)
            {
                canvas.DrawText(_glyph);
            }
            else if (_image is not null)
            {
                canvas.DrawImage(_image.Image, _image.Source, ViewBox);
            }
        }
        finally
        {
            canvas.Restore();
        }
    }
}
