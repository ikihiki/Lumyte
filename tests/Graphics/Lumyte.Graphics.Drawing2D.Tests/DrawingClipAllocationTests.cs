using System.Numerics;
using System.Runtime.CompilerServices;
using Lumyte.Graphics.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Measures actual scissor branches, including nonempty immutable clip enumeration.</summary>
[Collection("Drawing profiling")]
public sealed class DrawingClipAllocationTests(ITestOutputHelper output)
{
    private static byte[]? _allocationSink;

    /// <summary>Nonempty clips allocate neither with a command nor with a standalone clip state.</summary>
    /// <param name="withCommand">Whether command bounds are intersected first.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NonemptyClipAllocatesNothing(bool withCommand)
    {
        var path = new PathBuilder2D();
        path.MoveTo(new(10, 20));
        path.LineTo(new(30, 20));
        path.LineTo(new(30, 40));
        path.Close();
        Path2D shape = path.Build();
        var state = new DrawingState2D(Matrix3x2.Identity, new ClipStack2D([
            new(shape, Matrix3x2.CreateTranslation(5, 7), FillRule2D.NonZero),
            new(shape, Matrix3x2.CreateTranslation(8, 9), FillRule2D.EvenOdd),
        ]));
        DrawingCommand2D? command = withCommand ? new FillRectCommand2D(new(0, 0, 100, 100), PaintGraph2D.Solid(new(1, 1, 1, 1)), state) : null;
        Measure(command, state, null, false, false, null, new(17, 28, 19, 20));
    }

    /// <summary>Empty, absent, normalized and source-policy branches have the same strict zero budget.</summary>
    /// <param name="branch">The branch to exercise.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void OtherScissorBranchesAllocateNothing(int branch)
    {
        var state = new DrawingState2D(Matrix3x2.Identity, ClipStack2D.Empty);
        DrawingCommand2D? command = branch == 0 ? new FillRectCommand2D(new(10, 20, 30, 40), PaintGraph2D.Solid(new(1, 1, 1, 1)), state) : null;
        Rect2D? source = branch >= 3 ? new(20, 30, 40, 50) : null;
        ScissorRect? normalized = branch == 2 ? new(3, 4, 5, 6) : null;
        ScissorRect expected = branch switch
        {
            0 => new(19, 39, 62, 82),
            2 => new(3, 4, 5, 6),
            3 => new(19, 29, 42, 52),
            _ => new(0, 0, 256, 256),
        };
        Measure(command, branch == 1 ? state : null, source, branch == 4, branch == 5, normalized, expected);
    }

    /// <summary>The no-GC interval still reports real escaping allocations rather than masking the counter.</summary>
    [Fact]
    public void MeasurementIsolationDetectsRealAllocations()
    {
        _allocationSink = AllocateEscapingBuffer();
        long allocated;
        using (new DrawingAllocationScope())
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            _allocationSink = AllocateEscapingBuffer();
            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        }

        output.WriteLine($"Escaping allocation control: {allocated} bytes, architecture={System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
        Assert.True(allocated > 0);
        GC.KeepAlive(_allocationSink);
        _allocationSink = null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static byte[] AllocateEscapingBuffer() => new byte[64];

    private void Measure(DrawingCommand2D? command, DrawingState2D? state, Rect2D? source, bool hasOriginal, bool replace, ScissorRect? normalized, ScissorRect expected)
    {
        const int Iterations = 1000;
        DrawingTargetInfo2D target = new() { PixelScale = 2, Description = new() { Width = 256, Height = 256, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment } };

        // Nonempty fixtures use unit pixels so their independent intersection is easy to audit.
        if (state is not null && state.Clip.Elements.Count != 0)
        {
            target = target with { PixelScale = 1 };
        }

        for (int i = 0; i < Iterations; i++)
        {
            _ = DrawingBounds2D.GetScissor(command, state, target, source, hasOriginal, replace, normalized);
        }

        for (int window = 0; window < 3; window++)
        {
            using var measurement = new DrawingAllocationScope();
            ScissorRect actual = default;
            int threadBefore = Environment.CurrentManagedThreadId;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < Iterations; i++)
            {
                actual = DrawingBounds2D.GetScissor(command, state, target, source, hasOriginal, replace, normalized);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            int threadAfter = Environment.CurrentManagedThreadId;

            // Formatting, diagnostics and assertions stay outside the measured interval.
            output.WriteLine($"Window {window}: {allocated} bytes / {Iterations} calls = {(double)allocated / Iterations} B/call; command={command?.GetType().Name ?? "none"}, clips={state?.Clip.Elements.Count ?? 0}, normalized={normalized.HasValue}, thread={threadBefore}/{threadAfter}, architecture={System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
            Assert.Equal(threadBefore, threadAfter);
            Assert.Equal(expected, actual);
            Assert.Equal(0, allocated);
        }
    }
}
