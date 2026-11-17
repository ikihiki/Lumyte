using System.Numerics;
using System.Text;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Svg;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Exercises real usvg parsing through existing 2D command and geometry compilation.</summary>
public sealed class SvgTests
{
    /// <summary>CSS, use, arcs, viewBox, fill rules and transforms survive the native boundary.</summary>
    [Fact]
    public void ParsesResolvedGeometryAndReplaysWithoutChangingState()
    {
        SvgDocument2D svg = Parse("""
            <svg xmlns="http://www.w3.org/2000/svg" width="40" height="20" viewBox="0 0 20 10">
            <style>.icon {fill:red;fill-rule:evenodd}</style>
            <defs><path id="p" class="icon" d="M1 1a2 2 0 0 1 4 0L5 5Z"/></defs>
            <use href="#p" x="2"/></svg>
            """);
        Assert.Equal(new Vector2(40, 20), svg.Size);
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.Translate(new(3, 4));
        svg.Draw(canvas);
        canvas.FillRect(new(0, 0, 1, 1), new Lumyte.Mathematics.Colors.SRgba<float>(0, 0, 0, 1));
        DrawList2D list = canvas.End();
        FillPathCommand2D path = Assert.IsType<FillPathCommand2D>(list.Commands[0]);
        Assert.Equal(FillRule2D.EvenOdd, path.Rule);
        Assert.Contains(path.Path.Segments, segment => segment.Verb == PathVerb2D.Cubic);
        Assert.Equal(new Matrix3x2(2, 0, 0, 2, 7, 4), path.State.Transform);
        Assert.Single(path.State.Clip.Elements);
        Assert.Empty(list.Commands[1].State.Clip.Elements);
        Assert.Equal(Matrix3x2.CreateTranslation(3, 4), list.Commands[1].State.Transform);
        Assert.Equal(1, Assert.IsType<SolidPaintNode2D>(path.Paint.Root).Color.R);
        List<PathFlattening2D.Contour> contours = PathFlattening2D.Flatten(path.Path, path.State.Transform);
        Assert.NotEmpty(contours);
        var program = new DrawingProgram2D(new ArgumentTablePlan(new ProfilingDevice()).CreateShaderResources(), new DrawingTargetInfo2D
        {
            Description = new TextureDesc { Width = 64, Height = 64, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment },
        });
        program.Compile(path);
        program.ValidateFiniteData();
        Assert.NotEmpty(program.Instructions);
        Assert.True(contours[0].Closed);
    }

    /// <summary>Solid strokes retain width, joins, caps and SVG paint order.</summary>
    [Fact]
    public void PreservesStrokeAndPaintOrder()
    {
        SvgDocument2D svg = Parse("<svg xmlns='http://www.w3.org/2000/svg' width='10' height='10'><path d='M1 1L8 8' fill='red' stroke='blue' stroke-width='2' stroke-linecap='round' stroke-linejoin='bevel' paint-order='stroke fill'/></svg>");
        var canvas = new Canvas2D();
        canvas.Begin();
        svg.Draw(canvas);
        DrawList2D list = canvas.End();
        StrokePathCommand2D stroke = Assert.IsType<StrokePathCommand2D>(list.Commands[0]);
        Assert.Equal(2, stroke.Style.Width);
        Assert.Equal(LineCap2D.Round, stroke.Style.Cap);
        Assert.Equal(LineJoin2D.Bevel, stroke.Style.Join);
        Assert.IsType<FillPathCommand2D>(list.Commands[1]);
        Assert.NotEmpty(PathFlattening2D.Stroke(PathFlattening2D.Flatten(stroke.Path, Matrix3x2.Identity), stroke.Style, stroke.State.Transform));
    }

    /// <summary>Unrepresentable normalized features fail rather than returning a partial drawing.</summary>
    /// <param name="content">The rejected content.</param>
    /// <param name="diagnostic">The explicit diagnostic fragment.</param>
    [Theory]
    [InlineData("<g opacity='.5'><rect width='8' height='8'/></g>", "opacity")]
    [InlineData("<path d='M0 0L8 8' stroke='red' stroke-dasharray='1 2'/>", "dash")]
    [InlineData("<defs><linearGradient id='g'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)'/>", "gradient")]
    [InlineData("<rect width='8' height='8' shape-rendering='crispEdges'/>", "shape-rendering")]
    [InlineData("<rect width='8' height='8' shape-rendering='optimizeSpeed'/>", "shape-rendering")]
    [InlineData("<path d='M0 0L8 8' vector-effect='non-scaling-stroke'/>", "vector-effect")]
    [InlineData("<defs><clipPath id='c'><rect width='4' height='4'/></clipPath></defs><rect width='8' height='8' clip-path='url(#c)'/>", "clip")]
    [InlineData("<defs><mask id='m'><rect width='4' height='4' fill='white'/></mask></defs><rect width='8' height='8' mask='url(#m)'/>", "mask")]
    [InlineData("<defs><filter id='f'><feGaussianBlur stdDeviation='1'/></filter></defs><rect width='8' height='8' filter='url(#f)'/>", "filter")]
    [InlineData("<rect width='8' height='8' style='mix-blend-mode:multiply'/>", "blend")]
    [InlineData("<text>hello</text>", "text")]
    [InlineData("<image href='https://example.invalid/image.png'/>", "image")]
    [InlineData("<image href='file:///etc/passwd'/>", "image")]
    [InlineData("<foreignObject/>", "foreignObject")]
    [InlineData("<script/>", "script")]
    public void RejectsUnsupported(string content, string diagnostic)
    {
        FormatException error = Assert.Throws<FormatException>(() => Parse($"<svg xmlns='http://www.w3.org/2000/svg' width='10' height='10'>{content}</svg>"));
        Assert.Contains(diagnostic, error.Message);
    }

    /// <summary>Malformed input, UTF-8 and budgets have deterministic failures.</summary>
    [Fact]
    public void RejectsInvalidInputAndReleasesNativeResults()
    {
        Assert.Throws<FormatException>(() => SvgDocument2D.Parse([]));
        Assert.Throws<FormatException>(() => SvgDocument2D.Parse([255]));
        Assert.Throws<ArgumentOutOfRangeException>(() => SvgDocument2D.Parse(new byte[(4 * 1024 * 1024) + 1]));
        for (int i = 0; i < 100; i++)
        {
            Assert.Throws<FormatException>(() => Parse("bad XML"));
        }

        Assert.Equal(new Vector2(10, 10), Parse("<svg xmlns='http://www.w3.org/2000/svg' width='10' height='10'/>").Size);
    }

    /// <summary>Parsed paths still respect the downstream transform-dependent geometry budget.</summary>
    [Fact]
    public void DownstreamGeometryBudgetRemainsExplicit()
    {
        var xml = new StringBuilder("<svg xmlns='http://www.w3.org/2000/svg' width='10' height='10'><path d='M0 0");
        for (int i = 0; i < 65537; i++)
        {
            xml.Append(i % 2 == 0 ? "L1 1" : "L2 1");
        }

        xml.Append("'/></svg>");
        SvgDocument2D svg = Parse(xml.ToString());
        var canvas = new Canvas2D();
        canvas.Begin();
        svg.Draw(canvas);
        FillPathCommand2D command = Assert.IsType<FillPathCommand2D>(Assert.Single(canvas.End().Commands));
        Assert.Throws<NotSupportedException>(() => PathFlattening2D.Flatten(command.Path, command.State.Transform));
    }

    private static SvgDocument2D Parse(string xml) => SvgDocument2D.Parse(Encoding.UTF8.GetBytes(xml));
}
