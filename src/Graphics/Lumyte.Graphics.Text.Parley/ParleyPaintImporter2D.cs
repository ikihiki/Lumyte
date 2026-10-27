using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Text.Parley.Interop;

namespace Lumyte.Graphics.Text.Parley;

internal sealed unsafe class ParleyPaintImporter2D(LpLayoutView view, Matrix3x2 glyphTransform, PaintGraph2D foreground)
{
    private readonly Stack<Matrix3x2> _transforms = [];
    private readonly List<(Path2D Path, Rect2D Bounds)> _clips = [];
    private readonly Stack<(PaintEntry? Backdrop, CompositeMode2D Mode)> _layers = [];
    private Matrix3x2 _transform = glyphTransform;
    private PaintEntry? _current;

    public PaintEntry Import(uint start, uint count)
    {
        ReadOnlySpan<LpPaintEvent> paints = ParleyLayoutImporter2D.Buffer(view.paints, view.paint_count);
        ReadOnlySpan<LpPathSegment> paths = ParleyLayoutImporter2D.Buffer(view.paths, view.path_count);
        foreach (LpPaintEvent paint in ParleyLayoutImporter2D.Range(paints, start, count))
        {
            switch (paint.kind)
            {
                case 0:
                    _transforms.Push(_transform);
                    _transform = new Matrix3x2(paint.p0, paint.p1, paint.p2, paint.p3, paint.p4, paint.p5) * _transform;
                    break;
                case 1:
                    if (!_transforms.TryPop(out _transform))
                    {
                        throw new InvalidDataException("The native paint transform stack is unbalanced.");
                    }

                    break;
                case 2:
                    _clips.Add(ParleyLayoutImporter2D.Path(paths, paint.index, paint.count, _transform));
                    break;
                case 3:
                    _clips.Add(ParleyLayoutImporter2D.Rectangle(new Rect2D(paint.p0, paint.p1, MathF.Max(0, paint.p2 - paint.p0), MathF.Max(0, paint.p3 - paint.p1)), _transform));
                    break;
                case 4:
                    if (_clips.Count == 0)
                    {
                        throw new InvalidDataException("The native paint clip stack is unbalanced.");
                    }

                    _clips.RemoveAt(_clips.Count - 1);
                    break;
                case 5:
                    Add(paint.mode != 0 ? PaintGraph2D.Opacity(paint.p3, foreground) : Transform(PaintGraph2D.Solid(new Color2D(paint.p0, paint.p1, paint.p2, paint.p3))));
                    break;
                case 6:
                case 7:
                case 8:
                    Add(Gradient(paint));
                    break;
                case 9:
                    if (paint.mode > 27)
                    {
                        throw new InvalidDataException("The native color glyph uses an unknown composite mode.");
                    }

                    _layers.Push((_current, (CompositeMode2D)paint.mode));
                    _current = null;
                    break;
                case 10:
                    if (!_layers.TryPop(out (PaintEntry? Backdrop, CompositeMode2D Mode) layer))
                    {
                        throw new InvalidDataException("The native paint layer stack is unbalanced.");
                    }

                    _current = Composite(_current ?? Empty(), layer.Backdrop ?? Empty(), layer.Mode);
                    break;
                default:
                    throw new NotSupportedException($"The native paint opcode {paint.kind} is not supported.");
            }
        }

        if (_transforms.Count != 0 || _clips.Count != 0 || _layers.Count != 0)
        {
            throw new InvalidDataException("The native color glyph ended with an unbalanced paint stack.");
        }

        return _current ?? Empty();
    }

    private static PaintEntry Empty() => new(PaintGraph2D.Solid(default), default(Rect2D));

    private static PaintEntry Composite(PaintEntry source, PaintEntry backdrop, CompositeMode2D mode)
    {
        Rect2D? bounds = mode switch
        {
            CompositeMode2D.Clear => default(Rect2D),
            CompositeMode2D.Source or CompositeMode2D.SourceOut or CompositeMode2D.DestinationAtop => source.Bounds,
            CompositeMode2D.Destination or CompositeMode2D.DestinationOut or CompositeMode2D.SourceAtop => backdrop.Bounds,
            CompositeMode2D.SourceIn or CompositeMode2D.DestinationIn => ParleyLayoutImporter2D.Intersect(source.Bounds, backdrop.Bounds),
            _ => source.Bounds is Rect2D a && backdrop.Bounds is Rect2D b ? ParleyLayoutImporter2D.Union(a, b) : null,
        };
        return new PaintEntry(PaintGraph2D.Composite(source.Paint, backdrop.Paint, mode), bounds);
    }

    private static PaintGraph2D CreateGradient(LpPaintEvent paint, ColorStop2D[] stops)
        => paint.kind switch
        {
            6 => PaintGraph2D.LinearGradient(new Vector2(paint.p0, paint.p1), new Vector2(paint.p2, paint.p3), new Vector2(paint.p4, paint.p5), stops, (ExtendMode2D)paint.mode),
            7 => PaintGraph2D.RadialGradient(new Vector2(paint.p0, paint.p1), paint.p2, new Vector2(paint.p3, paint.p4), paint.p5, stops, (ExtendMode2D)paint.mode),
            8 => PaintGraph2D.SweepGradient(new Vector2(paint.p0, paint.p1), paint.p2, paint.p3, stops, (ExtendMode2D)paint.mode),
            _ => throw new InvalidDataException("The native paint is not a gradient."),
        };

    private PaintGraph2D Transform(PaintGraph2D paint)
        => _transform.IsIdentity ? paint : PaintGraph2D.Transform(_transform, paint);

    private void Add(PaintGraph2D paint)
    {
        if (!Matrix3x2.Invert(_transform, out _))
        {
            // A singular COLR transform also suppresses currentColor, whose
            // ordinary evaluation deliberately remains in paragraph coordinates.
            paint = PaintGraph2D.Solid(default);
        }

        Rect2D? bounds = null;
        foreach ((Path2D path, Rect2D clipBounds) in _clips)
        {
            paint = PaintGraph2D.Clip(path, paint);
            bounds = ParleyLayoutImporter2D.Intersect(bounds, clipBounds);
        }

        var source = new PaintEntry(paint, bounds);
        _current = _current is PaintEntry backdrop ? Composite(source, backdrop, CompositeMode2D.SourceOver) : source;
    }

    private PaintGraph2D Gradient(LpPaintEvent paint)
    {
        if (paint.mode > 2)
        {
            throw new InvalidDataException("The native color glyph uses an unknown gradient extension.");
        }

        ReadOnlySpan<LpColorStop> nativeStops = ParleyLayoutImporter2D.Range(ParleyLayoutImporter2D.Buffer(view.stops, view.stop_count), paint.index, paint.count);
        var colors = new ColorStop2D[nativeStops.Length];
        var weights = new ColorStop2D[nativeStops.Length];
        bool hasForeground = false;
        for (int i = 0; i < nativeStops.Length; i++)
        {
            LpColorStop stop = nativeStops[i];
            bool isForeground = stop.is_foreground != 0;
            hasForeground |= isForeground;
            colors[i] = new ColorStop2D(stop.offset, isForeground ? default : new Color2D(stop.r, stop.g, stop.b, stop.a));
            weights[i] = new ColorStop2D(stop.offset, new Color2D(1, 1, 1, isForeground ? stop.a : 0));
        }

        PaintGraph2D result = Transform(CreateGradient(paint, colors));
        if (!hasForeground)
        {
            return result;
        }

        // Transform only the alpha weights. currentColor stays in paragraph coordinates,
        // including when a COLR ancestor transforms the glyph or its gradient.
        PaintGraph2D mask = Transform(CreateGradient(paint, weights));
        var weightedForeground = PaintGraph2D.Composite(foreground, mask, CompositeMode2D.SourceIn);
        return PaintGraph2D.Composite(result, weightedForeground, CompositeMode2D.Plus);
    }

    internal readonly record struct PaintEntry(PaintGraph2D Paint, Rect2D? Bounds);
}
