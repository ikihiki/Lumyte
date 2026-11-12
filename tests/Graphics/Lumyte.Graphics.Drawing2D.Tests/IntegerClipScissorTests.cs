using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks deterministic CPU normalization and the sixteen-bit sample oracle.</summary>
public sealed class IntegerClipScissorTests
{
    /// <summary>Integer intersections have no residual GPU coverage instructions.</summary>
    [Fact]
    public void IntegerIntersectionNeedsNoCoverage()
    {
        DrawingCommand2D command = Command(canvas =>
        {
            canvas.ClipRect(new(2, 3, 20, 20));
            canvas.ClipRect(new(5, 1, 10, 10));
        });
        DrawingProgram2D program = Program();
        program.Compile(command);
        Assert.Equal(NormalizedClipKind2D.Scissor, program.Clip!.Kind);
        Assert.Equal(new ScissorRect(5, 3, 10, 8), program.Clip.Bounds);
        Assert.Equal(3, program.Instructions.Count);
        Assert.Empty(program.Data);
        Assert.DoesNotContain(program.Instructions, instruction => instruction.Kind is 1 or 4 or 16 or 17);
    }

    /// <summary>Fractional stacks become exactly one sample-grid coverage instruction.</summary>
    [Fact]
    public void FractionalIntersectionHasOneResidual()
    {
        DrawingCommand2D command = Command(canvas =>
        {
            canvas.ClipRect(new(0.2f, 0.3f, 16, 16));
            canvas.ClipRect(new(0.4f, 0.6f, 10, 10));
        });
        DrawingProgram2D program = Program();
        program.Compile(command);
        Assert.Equal(NormalizedClipKind2D.Analytic, program.Clip!.Kind);
        DrawingInstruction clip = Assert.Single(program.Instructions, instruction => instruction.Kind is 16 or 17);
        Assert.Equal(16u, clip.Kind);
        Assert.Equal(new Vector4(2, 2, 42, 42), clip.A);
        Assert.DoesNotContain(program.Instructions, instruction => instruction.Kind == 4);
        Assert.Empty(program.Data);
    }

    /// <summary>A triangle's sixteen predicates have an independently specified exact bit pattern.</summary>
    /// <param name="rule">The fill rule.</param>
    [Theory]
    [InlineData(FillRule2D.NonZero)]
    [InlineData(FillRule2D.EvenOdd)]
    public void TriangleMaskPreservesIndividualSamples(FillRule2D rule)
    {
        var path = new PathBuilder2D();
        path.MoveTo(Vector2.Zero);
        path.LineTo(Vector2.UnitX);
        path.LineTo(Vector2.UnitY);
        path.Close();
        ClipElement2D[] elements = new[] { new ClipElement2D(path.Build(), Matrix3x2.Identity, rule) };
        var clip = NormalizedClip2D.Create(elements, Target());
        Assert.Equal(NormalizedClipKind2D.SampleMask, clip.Kind);
        Assert.Equal(new ScissorRect(0, 0, 1, 1), clip.Bounds);
        Assert.Equal(new Vector4(0x0137, 0, 0, 0), Assert.Single(clip.Mask));
        Assert.Equal(6, BitOperations.PopCount((uint)clip.Mask[0].X));
    }

    /// <summary>Cache hit, miss and independent reconstruction never change representation or sample bits.</summary>
    [Fact]
    public void CacheDoesNotChooseExecutionSemantics()
    {
        DrawingCommand2D command = Command(canvas =>
        {
            canvas.Rotate(0.2f);
            canvas.ClipRect(new(1, 1, 8, 8));
        });
        var cache = new ClipNormalizationCache2D(Target());
        NormalizedClip2D first = cache.Get(command.State.Clip, Matrix3x2.Identity, command.State.Clip.Elements);
        Assert.Same(first, cache.Get(command.State.Clip, Matrix3x2.Identity, command.State.Clip.Elements));
        var fresh = NormalizedClip2D.Create(command.State.Clip.Elements, Target());
        Assert.Equal(first.Kind, fresh.Kind);
        Assert.Equal(first.Bounds, fresh.Bounds);
        Assert.Equal(first.Mask, fresh.Mask);
        Assert.Equal(NormalizedClipKind2D.SampleMask, fresh.Kind);
    }

    /// <summary>Empty clips finish before compiling shape or paint; no clip remains trivial.</summary>
    [Fact]
    public void EmptyAndNoClipEmitNoCoverageInstructions()
    {
        DrawingProgram2D empty = Program();
        empty.Compile(Command(canvas =>
        {
            canvas.ClipRect(new(1, 1, 2, 2));
            canvas.ClipRect(new(10, 10, 2, 2));
        }));
        Assert.Equal(NormalizedClipKind2D.Empty, empty.Clip!.Kind);
        Assert.Equal(Vector4.Zero, Assert.Single(empty.Instructions).A);
        Assert.Empty(empty.Data);
        DrawingProgram2D none = Program();
        none.Compile(Command(_ => { }));
        Assert.Equal(NormalizedClipKind2D.None, none.Clip!.Kind);
        Assert.Equal(3, none.Instructions.Count);
    }

    /// <summary>Target origin and DPR are semantic inputs, not cache tuning parameters.</summary>
    [Fact]
    public void TargetGridParticipatesInNormalization()
    {
        DrawingCommand2D command = Command(canvas => canvas.ClipRect(new(3, 4, 8, 8)));
        var normal = NormalizedClip2D.Create(command.State.Clip.Elements, Target());
        var scaled = NormalizedClip2D.Create(command.State.Clip.Elements, Target() with { PixelScale = 2, PixelOrigin = new(4, 6) });
        Assert.Equal(new ScissorRect(3, 4, 8, 8), normal.Bounds);
        Assert.Equal(new ScissorRect(2, 2, 16, 16), scaled.Bounds);
    }

    /// <summary>Known-empty scopes allocate no GPU payloads or logical tables.</summary>
    /// <returns>The asynchronous preparation.</returns>
    [Fact]
    public async Task EmptyPreparationAllocatesNoPayloadAsync()
    {
        var device = new ProfilingDevice();
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.ClipRect(new(1, 1, 1, 1));
        canvas.ClipRect(new(8, 8, 1, 1));
        canvas.FillCircle(new(4), 8, new Color2D(1, 1, 1, 1));
        canvas.FillRect(new(0, 0, 8, 8), new Color2D(1, 1, 1, 1));
        using PreparedDraw2D prepared = await renderer.PrepareAsync(canvas.End(), new() { View = view });
        Assert.Equal(0, device.NativeBuffers);
        Assert.Empty(device.Tables);
    }

    /// <summary>Budget failures are deterministic rejection, never a GPU clip fallback.</summary>
    [Fact]
    public void OversizeMaskNeverFallsBack()
    {
        var path = new PathBuilder2D();
        path.MoveTo(Vector2.Zero);
        path.LineTo(new(2048, 0));
        path.LineTo(new(0, 2048));
        path.Close();
        ClipElement2D[] elements = [new(path.Build(), Matrix3x2.Identity, FillRule2D.NonZero)];
        var cache = new ClipNormalizationCache2D(Target() with { Description = Target().Description with { Width = 4096, Height = 4096 } });
        object key = new();
        Assert.Throws<NotSupportedException>(() => cache.Get(key, Matrix3x2.Identity, elements));
        Assert.Throws<NotSupportedException>(() => cache.Get(key, Matrix3x2.Identity, elements));
    }

    /// <summary>Even-odd holes survive composition rather than becoming a bounding-box proof.</summary>
    [Fact]
    public void HoleMaskHasIndependentInteriorOracle()
    {
        var path = new PathBuilder2D();
        foreach (Rect2D rect in new[] { new Rect2D(0, 0, 4, 4), new Rect2D(1, 1, 2, 2) })
        {
            path.MoveTo(new(rect.X, rect.Y));
            path.LineTo(new(rect.X + rect.Width, rect.Y));
            path.LineTo(new(rect.X + rect.Width, rect.Y + rect.Height));
            path.LineTo(new(rect.X, rect.Y + rect.Height));
            path.Close();
        }

        var clip = NormalizedClip2D.Create([new(path.Build(), Matrix3x2.Identity, FillRule2D.EvenOdd)], Target());
        Assert.Equal(NormalizedClipKind2D.SampleMask, clip.Kind);
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                int index = (row * 4) + column;
                Assert.Equal(row is 1 or 2 && column is 1 or 2 ? 0f : 65535f, clip.Mask[index / 4][index % 4]);
            }
        }
    }

    /// <summary>Adjacent floating-point values on a sample edge follow the CPU grid oracle exactly.</summary>
    [Fact]
    public void AdjacentFloatBoundaryIsDeterministic()
    {
        DrawingCommand2D exact = Command(canvas => canvas.ClipRect(new(0.125f, 0, 1, 1)));
        DrawingCommand2D next = Command(canvas => canvas.ClipRect(new(MathF.BitIncrement(0.125f), 0, 1, 1)));
        var a = NormalizedClip2D.Create(exact.State.Clip.Elements, Target());
        var b = NormalizedClip2D.Create(next.State.Clip.Elements, Target());
        Assert.Equal(NormalizedClipKind2D.Scissor, a.Kind);
        Assert.Equal(NormalizedClipKind2D.Analytic, b.Kind);
        Assert.Equal(0f, a.Samples.X);
        Assert.Equal(1f, b.Samples.X);
    }

    /// <summary>Opacity separates logical clip scopes; adjacent clips merge without crossing it.</summary>
    [Fact]
    public void PaintClipScopesDoNotCrossOpacity()
    {
        var path = new PathBuilder2D();
        path.MoveTo(Vector2.Zero);
        path.LineTo(Vector2.UnitX);
        path.LineTo(Vector2.UnitY);
        path.Close();
        Path2D triangle = path.Build();
        var solid = PaintGraph2D.Solid(new(1, 1, 1, 1));
        var inner = PaintGraph2D.Clip(triangle, solid);
        var separated = PaintGraph2D.Clip(triangle, PaintGraph2D.Opacity(0.5f, inner));
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new(0, 0, 1, 1), separated);
        DrawingProgram2D program = Program();
        program.Compile(Assert.Single(canvas.End().Commands));
        DrawingInstruction[] clips = program.Instructions.Where(instruction => instruction.Kind == 17).ToArray();
        Assert.Equal(2, clips.Length);
        Assert.Equal(10u, program.Instructions[(int)clips[1].First].Kind);
        Assert.Equal(clips[0].Kind, program.Instructions[(int)program.Instructions[(int)clips[1].First].First].Kind);
        canvas.Begin();
        canvas.FillRect(new(0, 0, 1, 1), PaintGraph2D.Clip(triangle, inner));
        DrawingProgram2D adjacent = Program();
        adjacent.Compile(Assert.Single(canvas.End().Commands));
        Assert.Single(adjacent.Instructions, instruction => instruction.Kind == 17);
    }

    private static DrawingCommand2D Command(Action<Canvas2D> clip)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        clip(canvas);
        canvas.FillCircle(new(16, 16), 32, new Color2D(1, 0, 0, 1));
        return Assert.Single(canvas.End().Commands);
    }

    private static DrawingProgram2D Program() => new(new ArgumentTablePlan(new ProfilingDevice()).CreateShaderResources(), Target());

    private static DrawingTargetInfo2D Target()
        => new() { Description = new() { Width = 32, Height = 32, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment } };
}
