using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Deterministic input-only capacity admission; no device or timing oracle.</summary>
public sealed class DrawingWorkingBudgetTests
{
    private static readonly PaintGraph2D _white = PaintGraph2D.Solid(new SRgba<float>(1, 1, 1, 1));
    private static readonly DrawingState2D _state = new(Matrix3x2.Identity, ClipStack2D.Empty);
    private static readonly DrawingTargetInfo2D _target = new()
    {
        Description = new() { Width = 64, Height = 64, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment },
    };

    /// <summary>Many ordinary contours no longer consume 32 MiB each.</summary>
    [Fact]
    public void ManySimplePathsFitOneMiB()
    {
        Path2D path = Triangle();
        var content = new DrawList2D(Enumerable.Range(0, 64)
            .Select(_ => new FillPathCommand2D(path, _white, FillRule2D.NonZero, _state)));
        DrawingWorkingBudget2D.Validate(content, _target, 1024 * 1024);
    }

    /// <summary>Line and path strokes reserve body, join, cap and isolated-point capacity.</summary>
    /// <param name="cap">The cap style.</param>
    /// <param name="join">The join style.</param>
    [Theory]
    [InlineData(LineCap2D.Butt, LineJoin2D.Miter)]
    [InlineData(LineCap2D.Square, LineJoin2D.Bevel)]
    [InlineData(LineCap2D.Round, LineJoin2D.Round)]
    public void ManySmallStrokesFitModestBudget(LineCap2D cap, LineJoin2D join)
    {
        var style = new StrokeStyle2D { Width = 2, Cap = cap, Join = join };
        Path2D path = Triangle();
        var content = new DrawList2D(Enumerable.Range(0, 32).SelectMany(_ => new DrawingCommand2D[]
        {
            new DrawLineCommand2D(Vector2.Zero, Vector2.One, _white, style, _state),
            new StrokePathCommand2D(path, _white, style, _state),
        }));
        DrawingWorkingBudget2D.Validate(content, _target, 4 * 1024 * 1024);
    }

    /// <summary>Bezier bounds remain practical and respond to device-space scale.</summary>
    [Fact]
    public void CurvesFitSmallBudgetButScaleIncreasesReservation()
    {
        var builder = new PathBuilder2D();
        builder.MoveTo(Vector2.Zero);
        builder.QuadraticTo(new(0, 16), new(16, 0));
        builder.CubicTo(new(16, 16), new(0, 16), Vector2.Zero);
        Path2D path = builder.Build();
        var content = new DrawList2D([new FillPathCommand2D(path, _white, FillRule2D.NonZero, _state)]);
        DrawingWorkingBudget2D.Validate(content, _target, 64 * 1024);
        var scaled = new DrawList2D([new FillPathCommand2D(path, _white, FillRule2D.NonZero, _state with { Transform = Matrix3x2.CreateScale(256) })]);
        Assert.Throws<NotSupportedException>(() => DrawingWorkingBudget2D.Validate(scaled, _target, 64 * 1024));
    }

    /// <summary>Point limits and cumulative bytes are checked independently of actual flattening.</summary>
    [Fact]
    public void OversizedSourceAndLargePayloadAreRejected()
    {
        var segments = new PathSegment2D[65537];
        segments[0] = new(PathVerb2D.Move, Vector2.Zero, default, default);
        Array.Fill(segments, new(PathVerb2D.Line, Vector2.One, default, default), 1, 65536);
        var oversized = new DrawList2D([new FillPathCommand2D(new(segments), _white, FillRule2D.NonZero, _state)]);
        Assert.Throws<NotSupportedException>(() => DrawingWorkingBudget2D.Validate(oversized, _target, long.MaxValue));
        var bounded = new DrawList2D([new FillPathCommand2D(new(segments.Take(20000)), _white, FillRule2D.NonZero, _state)]);
        Assert.Throws<NotSupportedException>(() => DrawingWorkingBudget2D.Validate(bounded, _target, 1024 * 1024));
    }

    /// <summary>Huge finite inputs cannot wrap into small capacities or clamp round precision.</summary>
    [Fact]
    public void OverflowProneCurvesAndRoundStrokesAreRejected()
    {
        var builder = new PathBuilder2D();
        builder.MoveTo(Vector2.Zero);
        builder.QuadraticTo(new(0, float.MaxValue), Vector2.One);
        var curves = new DrawList2D([new FillPathCommand2D(builder.Build(), _white, FillRule2D.NonZero, _state)]);
        Assert.Throws<NotSupportedException>(() => DrawingWorkingBudget2D.Validate(curves, _target, long.MaxValue));
        var stroke = new DrawList2D([new DrawLineCommand2D(Vector2.Zero, Vector2.One, _white, new() { Width = float.MaxValue, Cap = LineCap2D.Round }, _state)]);
        Assert.Throws<NotSupportedException>(() => DrawingWorkingBudget2D.Validate(stroke, _target, long.MaxValue));
        var clipped = new DrawList2D([new FillPathCommand2D(Triangle(), _white, FillRule2D.NonZero, _state with { Clip = new([new(Triangle(), Matrix3x2.Identity, FillRule2D.NonZero)]) })]);
        DrawingTargetInfo2D hugeTarget = _target with { Description = _target.Description with { Width = uint.MaxValue, Height = uint.MaxValue } };
        Assert.Throws<NotSupportedException>(() => DrawingWorkingBudget2D.Validate(clipped, hugeTarget, long.MaxValue));
    }

    /// <summary>Reference aliases are charged once, but equal independently owned paints are not.</summary>
    [Fact]
    public void SharedPaintOwnerIsChargedOnce()
    {
        DrawList2D Shared(bool share) => new(Enumerable.Range(0, 32).Select(_ =>
            new FillRectCommand2D(new(0, 0, 1, 1), share ? _white : PaintGraph2D.Solid(new SRgba<float>(1, 1, 1, 1)), _state)));
        DrawingWorkingBudget2D.Validate(Shared(true), _target, 300000);
        Assert.Throws<NotSupportedException>(() => DrawingWorkingBudget2D.Validate(Shared(false), _target, 300000));
    }

    /// <summary>A different effective transform creates a different compiled paint owner.</summary>
    [Fact]
    public void SharedPaintWithDifferentTransformsIsNotMerged()
    {
        var content = new DrawList2D(new DrawingCommand2D[]
        {
            new FillRectCommand2D(new(0, 0, 1, 1), _white, _state),
            new FillRectCommand2D(new(0, 0, 1, 1), _white, _state with { Transform = Matrix3x2.CreateScale(2) }),
        });
        Assert.Throws<NotSupportedException>(() => DrawingWorkingBudget2D.Validate(content, _target, 27000));
    }

    /// <summary>A shared captured clip does not re-reserve its normalization owner per draw.</summary>
    [Fact]
    public void SharedSmallClipFitsModestBudget()
    {
        DrawingState2D clipped = _state with { Clip = new([new(Triangle(), Matrix3x2.Identity, FillRule2D.NonZero)]) };
        var content = new DrawList2D(Enumerable.Range(0, 32)
            .Select(_ => new FillPathCommand2D(Triangle(), _white, FillRule2D.NonZero, clipped)));
        DrawingWorkingBudget2D.Validate(content, _target, 512 * 1024);
    }

    /// <summary>Aliased children within one DAG do not become expanded ownership charges.</summary>
    [Fact]
    public void SharedDagChildrenAreChargedOnce()
    {
        var shared = PaintGraph2D.Layers(Enumerable.Repeat(_white, 32).ToArray());
        var content = new DrawList2D([new FillRectCommand2D(new(0, 0, 1, 1), shared, _state)]);
        DrawingWorkingBudget2D.Validate(content, _target, 30000);
        var separate = PaintGraph2D.Layers(Enumerable.Range(0, 32)
            .Select(_ => PaintGraph2D.Solid(new SRgba<float>(1, 1, 1, 1))).ToArray());
        var distinct = new DrawList2D([new FillRectCommand2D(new(0, 0, 1, 1), separate, _state)]);
        Assert.Throws<NotSupportedException>(() => DrawingWorkingBudget2D.Validate(distinct, _target, 30000));
    }

    /// <summary>Verbs with no generated endpoints still require raw encoding capacity.</summary>
    [Fact]
    public void RedundantClosingVerbsCannotEscapeRawInputBudget()
    {
        var segments = new PathSegment2D[10000];
        segments[0] = new(PathVerb2D.Move, Vector2.Zero, default, default);
        Array.Fill(segments, new(PathVerb2D.Close, default, default, default), 1, 9999);
        var content = new DrawList2D([new FillPathCommand2D(new(segments), _white, FillRule2D.NonZero, _state)]);
        Assert.Throws<NotSupportedException>(() => DrawingWorkingBudget2D.Validate(content, _target, 1024 * 1024));
    }

    private static Path2D Triangle()
    {
        var builder = new PathBuilder2D();
        builder.MoveTo(Vector2.Zero);
        builder.LineTo(new(8, 0));
        builder.LineTo(new(0, 8));
        builder.Close();
        return builder.Build();
    }
}
