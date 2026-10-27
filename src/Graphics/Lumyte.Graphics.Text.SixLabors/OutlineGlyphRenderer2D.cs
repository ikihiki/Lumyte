using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using SixLabors.Fonts;

namespace Lumyte.Graphics.Text;

internal sealed class OutlineGlyphRenderer2D(PaintGraph2D defaultPaint, IReadOnlyList<ColorFont2D> colorFonts, int colorPaletteIndex) : IGlyphRenderer, IColorGlyphRenderer
{
    private readonly PaintGraph2D _defaultPaint = defaultPaint;
    private readonly List<TextDrawing2D> _drawings = [];
    private readonly List<TextGeometry2D> _decorationGeometry = [];
    private readonly HashSet<(Vector2 Start, Vector2 End, float Thickness, PaintGraph2D Paint)> _decorationKeys = [];
    private PathBuilder2D _path = new();
    private PaintGraph2D _paint = defaultPaint;
    private PaintGraph2D _decorationPaint = defaultPaint;
    private TextDecorations _decorations;
    private Vector2 _minimum = new(float.PositiveInfinity);
    private Vector2 _maximum = new(float.NegativeInfinity);
    private ColorGlyphPaint2D? _colorGlyph;

    public IReadOnlyList<TextDrawing2D> Drawings => _drawings;

    public Rect2D Bounds => float.IsPositiveInfinity(_minimum.X) ? default : new(_minimum.X, _minimum.Y, _maximum.X - _minimum.X, _maximum.Y - _minimum.Y);

    public void BeginText(in FontRectangle bounds)
    {
    }

    public void EndText() => _drawings.AddRange(_decorationGeometry);

    public bool BeginGlyph(in FontRectangle bounds, in GlyphRendererParameters parameters)
    {
        _path = new PathBuilder2D();
        _paint = (parameters.TextRun as TextRun2D)?.Paint ?? _defaultPaint;
        _decorationPaint = _paint;
        _decorations = parameters.TextRun.TextDecorations;
        _colorGlyph = null;
        foreach (ColorFont2D font in colorFonts)
        {
            if (font.Matches(in parameters) && font.HasColorGlyph(parameters.GlyphId))
            {
                _colorGlyph = font.PaintGlyph(in bounds, in parameters, _paint, colorPaletteIndex);
                Rect2D rect = _colorGlyph.Bounds;
                Include(new Vector2(rect.X, rect.Y));
                Include(new Vector2(rect.X + rect.Width, rect.Y + rect.Height));
                break;
            }
        }

        return true;
    }

    public void SetColor(GlyphColor color) => _paint = PaintGraph2D.Solid(new Color2D(color.Red / 255f, color.Green / 255f, color.Blue / 255f, color.Alpha / 255f));

    public void EndGlyph()
    {
        Path2D path = _path.Build();
        if (_colorGlyph is not null)
        {
            _drawings.Add(new TextPaintGeometry2D(_colorGlyph.Paint, _colorGlyph.Bounds));
        }
        else if (path.Segments.Count > 0)
        {
            _drawings.Add(new TextGeometry2D(path, _paint));
        }
    }

    public void BeginFigure()
    {
    }

    public void EndFigure() => _path.Close();

    public void MoveTo(Vector2 point)
    {
        Include(point);
        _path.MoveTo(point);
    }

    public void LineTo(Vector2 point)
    {
        Include(point);
        _path.LineTo(point);
    }

    public void QuadraticBezierTo(Vector2 secondControlPoint, Vector2 point)
    {
        Include(secondControlPoint);
        Include(point);
        _path.QuadraticTo(secondControlPoint, point);
    }

    public void CubicBezierTo(Vector2 secondControlPoint, Vector2 thirdControlPoint, Vector2 point)
    {
        Include(secondControlPoint);
        Include(thirdControlPoint);
        Include(point);
        _path.CubicTo(secondControlPoint, thirdControlPoint, point);
    }

    public TextDecorations EnabledDecorations() => _decorations;

    public void SetDecoration(TextDecorations textDecorations, Vector2 start, Vector2 end, float thickness)
    {
        Vector2 direction = end - start;
        float length = direction.Length();
        if (length == 0 || thickness <= 0)
        {
            return;
        }

        Vector2 normal = new Vector2(-direction.Y, direction.X) * (thickness / (2 * length));
        Vector2 offset = textDecorations switch
        {
            TextDecorations.Underline => normal,
            TextDecorations.Overline => -normal,
            _ => Vector2.Zero,
        };
        start += offset;
        end += offset;
        if (!_decorationKeys.Add((start, end, thickness, _decorationPaint)))
        {
            return;
        }

        var builder = new PathBuilder2D();
        Vector2[] points = [start - normal, end - normal, end + normal, start + normal];
        builder.MoveTo(points[0]);
        Include(points[0]);
        for (int i = 1; i < points.Length; i++)
        {
            builder.LineTo(points[i]);
            Include(points[i]);
        }

        builder.Close();

        // Color fonts can repeat the same decoration for each layer. Composite it once,
        // after every glyph, so translucent paint does not accumulate or get covered by a later layer.
        _decorationGeometry.Add(new TextGeometry2D(builder.Build(), _decorationPaint));
    }

    private void Include(Vector2 point)
    {
        _minimum = Vector2.Min(_minimum, point);
        _maximum = Vector2.Max(_maximum, point);
    }
}
