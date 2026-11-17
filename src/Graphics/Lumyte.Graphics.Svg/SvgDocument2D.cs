using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Mathematics.Colors;

namespace Lumyte.Graphics.Svg;

/// <summary>An immutable, GPU-independent static SVG snapshot backed by existing 2D commands.</summary>
public sealed class SvgDocument2D
{
    private const int MaximumInputBytes = 4 * 1024 * 1024;
    private readonly DrawList2D _drawing;

    private SvgDocument2D(float width, float height, DrawList2D drawing)
    {
        Size = new(width, height);
        _drawing = drawing;
    }

    /// <summary>Gets the intrinsic viewport size in logical pixels (96 DPI).</summary>
    public Vector2 Size { get; }

    /// <summary>Parses UTF-8 XML with pinned usvg, rejecting unsupported normalized output.</summary>
    /// <param name="utf8">At most 4 MiB of uncompressed UTF-8 SVG. No files, network or fonts are loaded.</param>
    /// <returns>An independent managed snapshot; native storage is released before return.</returns>
    /// <exception cref="FormatException">Invalid SVG or an unsupported source element or normalized feature.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The input exceeds 4 MiB.</exception>
    public static unsafe SvgDocument2D Parse(ReadOnlySpan<byte> utf8)
    {
        if (utf8.Length > MaximumInputBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(utf8), "SVG input exceeds 4 MiB.");
        }

        // An empty span has a null fixed pointer; diagnose it consistently with malformed XML.
        if (utf8.IsEmpty)
        {
            throw new FormatException("SVG input is empty.");
        }

        nint document;
        fixed (byte* input = utf8)
        {
            document = NativeMethods.ls_parse(input, (nuint)utf8.Length);
        }

        if (document == 0)
        {
            throw new FormatException("Native SVG parser rejected the input arguments.");
        }

        try
        {
            byte* error = NativeMethods.ls_error(document, out nuint errorLength);
            if (errorLength != 0)
            {
                throw new FormatException(Encoding.UTF8.GetString(new ReadOnlySpan<byte>(error, checked((int)errorLength))));
            }

            float* data = NativeMethods.ls_words(document, out nuint count);
            return Import(new ReadOnlySpan<float>(data, checked((int)count)));
        }
        finally
        {
            NativeMethods.ls_free(document);
        }
    }

    /// <summary>Replays under the caller's current transform and clip without changing their state.</summary>
    /// <param name="canvas">An active pure or RenderGraph canvas recording.</param>
    public void Draw(DrawingCanvas2D canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        canvas.Replay(_drawing);
    }

    private static SvgDocument2D Import(ReadOnlySpan<float> words)
    {
        float width = words[0];
        float height = words[1];
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.ClipRect(new(0, 0, width, height));
        int index = 2;
        while (index < words.Length)
        {
            bool stroke = words[index++] != 0;
            var transform = new Matrix3x2(words[index], words[index + 1], words[index + 2], words[index + 3], words[index + 4], words[index + 5]);
            index += 6;
            var color = new SRgba<float>(words[index], words[index + 1], words[index + 2], words[index + 3]);
            index += 4;
            FillRule2D rule = words[index++] == 0 ? FillRule2D.NonZero : FillRule2D.EvenOdd;
            StrokeStyle2D? style = null;
            if (stroke)
            {
                style = new()
                {
                    Width = words[index],
                    Cap = words[index + 1] switch { 0 => LineCap2D.Butt, 1 => LineCap2D.Round, _ => LineCap2D.Square },
                    Join = words[index + 2] switch { 0 => LineJoin2D.Miter, 1 => LineJoin2D.Round, _ => LineJoin2D.Bevel },
                    MiterLimit = words[index + 3],
                };
                index += 4;
            }

            int segments = checked((int)words[index++]);
            var builder = new PathBuilder2D();
            for (int i = 0; i < segments; i++)
            {
                int verb = (int)words[index++];
                switch (verb)
                {
                    case 0:
                        builder.MoveTo(Point(words, ref index));
                        break;
                    case 1:
                        builder.LineTo(Point(words, ref index));
                        break;
                    case 2:
                        builder.QuadraticTo(Point(words, ref index), Point(words, ref index));
                        break;
                    case 3:
                        builder.CubicTo(Point(words, ref index), Point(words, ref index), Point(words, ref index));
                        break;
                    case 4:
                        builder.Close();
                        break;
                    default:
                        throw new FormatException("Invalid native SVG path verb.");
                }
            }

            canvas.Save();
            canvas.ConcatTransform(transform);
            var paint = PaintGraph2D.Solid(color);
            if (style is null)
            {
                canvas.FillPath(builder.Build(), paint, rule);
            }
            else
            {
                canvas.StrokePath(builder.Build(), paint, style);
            }

            canvas.Restore();
        }

        return new(width, height, canvas.End());
    }

    private static Vector2 Point(ReadOnlySpan<float> words, ref int index)
    {
        var point = new Vector2(words[index], words[index + 1]);
        index += 2;
        return point;
    }

    private static unsafe class NativeMethods
    {
        [DllImport("lumyte_svg_usvg", CallingConvention = CallingConvention.Cdecl)]
        internal static extern nint ls_parse(byte* data, nuint length);

        [DllImport("lumyte_svg_usvg", CallingConvention = CallingConvention.Cdecl)]
        internal static extern float* ls_words(nint document, out nuint length);

        [DllImport("lumyte_svg_usvg", CallingConvention = CallingConvention.Cdecl)]
        internal static extern byte* ls_error(nint document, out nuint length);

        [DllImport("lumyte_svg_usvg", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ls_free(nint document);
    }
}
