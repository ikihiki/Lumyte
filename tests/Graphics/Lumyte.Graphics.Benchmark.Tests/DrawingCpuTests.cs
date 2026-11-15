using Lumyte.Graphics.Benchmarks.Testing;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.Benchmark.Renderer;
using Xunit;

namespace Lumyte.Graphics.Benchmark.Tests;

/// <summary>Public renderer contracts on the managed recording device, without a GPU or pixel oracle.</summary>
public sealed class DrawingCpuTests
{
    /// <summary>Real rectangle batches split at the documented implementation boundary.</summary>
    /// <param name="count">The rectangle count.</param>
    /// <param name="draws">The expected actual draw calls.</param>
    [Theory]
    [InlineData(1023, 1)]
    [InlineData(1024, 1)]
    [InlineData(1025, 2)]
    public void RectangleBoundaryRecordsActualDraws(int count, long draws)
    {
        DrawList2D drawing = DrawingWorkloads.Rectangles(count);
        Assert.Equal(count, drawing.Count);
        using var harness = new DrawingCpuHarness();
        Assert.NotEqual(0UL, harness.ColdPrepareRecordDispose(drawing));
        CpuGraphicsCounters counters = harness.Device.Counters;
        Assert.Equal(draws, counters.Draws);
        Assert.Equal(draws, counters.RenderPasses);
        Assert.Equal(0, counters.Dispatches);
        Assert.Equal(0, counters.Submissions);
        Assert.Equal(count * 96L, counters.UploadedBytes);
        Assert.Equal(draws, counters.Copies);
        Assert.True(counters.ShaderCreates > 0);
        Assert.True(counters.PipelineCreates > 0);
        Assert.Equal(2, counters.LiveResources);
    }

    /// <summary>Warm renderer resources are bounded over repeated prepare/record/dispose waves and fully released on teardown.</summary>
    [Fact]
    public void RepeatedWavesReuseResourcesAndTeardown()
    {
        var harness = new DrawingCpuHarness();
        CpuGraphicsDevice device = harness.Device;
        DrawList2D drawing = DrawingWorkloads.Rectangles(1025);
        try
        {
            // The 1-instance tail rents a minimum-64-vector entry, outside its 2x reuse range.
            // Prime the documented three-entry idle pool before asserting its bounded plateau.
            for (int prime = 0; prime < 4; prime++)
            {
                harness.WarmPrepareRecordDispose(drawing);
            }

            long live = device.Counters.LiveResources;
            for (int wave = 0; wave < 12; wave++)
            {
                Assert.NotEqual(0UL, harness.WarmPrepareRecordDispose(drawing));
                CpuGraphicsCounters counters = device.Counters;
                Assert.Equal(2, counters.Draws);
                Assert.Equal(0, counters.ShaderCreates);
                Assert.Equal(0, counters.PipelineCreates);
                Assert.Equal(live, counters.LiveResources);
                Assert.True(counters.UploadedBytes > 0);
            }
        }
        finally
        {
            harness.Dispose();
        }

        Assert.Equal(0, device.Counters.LiveResources);
    }

    /// <summary>The bounded public path/clip/gradient fixture exercises real shader-data uploads and draw recording.</summary>
    [Fact]
    public void SharedGradientPathsProduceUploadsAndDraws()
    {
        DrawList2D drawing = DrawingWorkloads.ClippedGradientPaths();
        Assert.Equal(16, drawing.Count);
        using var harness = new DrawingCpuHarness();
        ulong checksum = harness.ColdPrepareRecordDispose(drawing);
        CpuGraphicsCounters counters = harness.Device.Counters;
        Assert.NotEqual(0UL, checksum);
        Assert.Equal(16, counters.Draws);

        // General drawing initializes its fallback texture in one additional clear-only pass.
        Assert.Equal(17, counters.RenderPasses);
        Assert.True(counters.UploadedBytes > 0);
        Assert.True(counters.BufferCreates > 0);
        Assert.Equal(0, counters.ComputePasses);
        Assert.Equal(0, counters.Submissions);
        Assert.Equal(2, counters.LiveResources);
    }

    /// <summary>Changing real drawing input changes consumed uploaded/recorded data rather than returning a constant checksum.</summary>
    [Fact]
    public void UploadedPayloadAffectsChecksum()
    {
        using var first = new DrawingCpuHarness();
        using var second = new DrawingCpuHarness();
        Assert.NotEqual(
            first.ColdPrepareRecordDispose(DrawingWorkloads.Rectangles(1023)),
            second.ColdPrepareRecordDispose(DrawingWorkloads.Rectangles(1024)));
    }
}
