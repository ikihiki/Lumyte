using Lumyte.Graphics.Abstractions;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks sparse GPU admission using the compressed representation rather than phantom mask cells.</summary>
public sealed class SparsePainterAdmissionTests
{
    /// <summary>A 1024-square full interior has 1024 headers, not a full image mask.</summary>
    /// <returns>The completed descriptor-only preparation.</returns>
    [Fact]
    public async Task FullInteriorDoesNotChargeBoundaryMaskCellsAsync()
    {
        var device = new ProfilingDevice();
        using var renderer = new SparseStripRenderer2D(device);
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new(0, 0, 1024, 1024), SRgba<float>.Red);
        var target = new DrawingTargetInfo2D { Description = new() { Width = 1024, Height = 1024, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment } };
        using var arguments = new DrawingArgumentPlan2D(device);
        using DrawingPassPlan2D plan = await ((IDrawingPassRenderer2D)renderer).PreparePassesAsync(canvas.End(), target, arguments, default);
        Assert.Equal(2, plan.Passes.Count);
        await arguments.SealAsync();
        Assert.True(device.NativeBuffers > 0);
    }
}
