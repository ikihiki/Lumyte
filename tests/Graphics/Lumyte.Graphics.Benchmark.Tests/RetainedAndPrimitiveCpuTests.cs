using Lumyte.Graphics.Benchmarks.Testing;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.Benchmark.Primitives;
using Lumyte.Graphics.Drawing2D.Benchmark.Retained;
using Xunit;

namespace Lumyte.Graphics.Benchmark.Tests;

/// <summary>Deterministic retained and primitive operation/lifetime contracts; never GPU or timing oracles.</summary>
public sealed class RetainedAndPrimitiveCpuTests
{
    /// <summary>Completed real retained frames publish residency and unchanged live/frozen preparations reuse every run without uploads.</summary>
    /// <param name="count">The scene placement count.</param>
    [Theory]
    [InlineData(64)]
    [InlineData(1024)]
    public void RetiredFramesReuseAllRunsAndReleaseResources(int count)
    {
        var fixture = new RetainedSceneFixture(count);
        CpuGraphicsDevice device = fixture.Device;
        try
        {
            Assert.NotEqual(0UL, fixture.PrepareRecordRetire());
            Assert.Equal(count, fixture.Statistics.CompiledRuns);
            Assert.Equal(count, fixture.Statistics.CachedAssets);
            Assert.Equal(0, fixture.Statistics.ActiveFrames);

            // Each independent resident run owns a 96-byte record and a 16-byte color payload.
            Assert.Equal(count * 112L, device.Counters.UploadedBytes);
            Assert.Equal(count, device.DrawnInstances);
            long live = device.Counters.LiveResources;
            for (int i = 0; i < 6; i++)
            {
                DrawingRetentionStatistics2D before = fixture.Statistics;
                Assert.NotEqual(0UL, fixture.PrepareRecordRetire(snapshot: i % 2 == 0));
                Assert.Equal(before.CompiledRuns, fixture.Statistics.CompiledRuns);
                Assert.Equal(count, fixture.Statistics.ReusedRuns - before.ReusedRuns);
                Assert.Equal(count, fixture.Statistics.CachedAssets);
                Assert.Equal(0, fixture.Statistics.ActiveFrames);
                Assert.Equal(0, device.Counters.UploadedBytes);
                Assert.Equal(count, device.Counters.Draws);
                Assert.Equal(1, device.Counters.Submissions);
                Assert.Equal(live, device.Counters.LiveResources);
            }
        }
        finally
        {
            fixture.Dispose();
        }

        Assert.Equal(0, device.Counters.LiveResources);
    }

    /// <summary>Every alternating edit changes the public scene revision, including finite insertion/removal and reorder cycles.</summary>
    /// <param name="mutation">The actual scene operation.</param>
    /// <param name="count">The base scene size.</param>
    [Theory]
    [InlineData(SceneMutation.Content, 64)]
    [InlineData(SceneMutation.Transform, 64)]
    [InlineData(SceneMutation.Clip, 64)]
    [InlineData(SceneMutation.InsertRemove, 64)]
    [InlineData(SceneMutation.Reorder, 64)]
    [InlineData(SceneMutation.Content, 1024)]
    [InlineData(SceneMutation.Transform, 1024)]
    [InlineData(SceneMutation.Clip, 1024)]
    [InlineData(SceneMutation.InsertRemove, 1024)]
    [InlineData(SceneMutation.Reorder, 1024)]
    public void SceneEditsNeverBecomeRepeatedNoOps(SceneMutation mutation, int count)
    {
        using var fixture = new RetainedSceneFixture(count);
        ulong revision;
        using (DrawingSceneReadLease2D source = fixture.Scene.Borrow())
        {
            revision = source.Revision;
        }

        for (int i = 0; i < 12; i++)
        {
            ulong changed = fixture.EditAndPublish(mutation);
            Assert.True(changed > revision);
            revision = changed;
        }

        Assert.Equal(0, fixture.Statistics.CompiledRuns);
        Assert.Equal(0, fixture.Device.Counters.UploadedBytes);
        Assert.Equal(0, fixture.Device.Counters.Submissions);
    }

    /// <summary>A single content/property edit recompiles exactly one run on every alternating wave, not merely the first wave.</summary>
    /// <param name="mutation">The single-placement invalidation.</param>
    /// <param name="count">The base placement count.</param>
    [Theory]
    [InlineData(SceneMutation.Content, 64)]
    [InlineData(SceneMutation.Transform, 64)]
    [InlineData(SceneMutation.Clip, 64)]
    [InlineData(SceneMutation.Content, 1024)]
    [InlineData(SceneMutation.Transform, 1024)]
    [InlineData(SceneMutation.Clip, 1024)]
    public void DirtyPlacementCompilesOneRunEveryWave(SceneMutation mutation, int count)
    {
        var fixture = new RetainedSceneFixture(count);
        CpuGraphicsDevice device = fixture.Device;
        try
        {
            fixture.PrepareRecordRetire();
            long[] plateaus = new long[2];
            for (int i = 0; i < 12; i++)
            {
                fixture.EditAndPublish(mutation);
                DrawingRetentionStatistics2D before = fixture.Statistics;
                fixture.PrepareRecordRetire();
                Assert.Equal(1, fixture.Statistics.CompiledRuns - before.CompiledRuns);
                Assert.Equal(count - 1, fixture.Statistics.ReusedRuns - before.ReusedRuns);
                Assert.Equal(count, fixture.Statistics.CachedAssets);
                Assert.Equal(0, fixture.Statistics.ActiveFrames);
                Assert.True(device.Counters.UploadedBytes > 0);
                if (mutation != SceneMutation.Clip)
                {
                    Assert.Equal(112, device.Counters.UploadedBytes);
                }

                Assert.Equal(1, device.Counters.Submissions);
                if (i is 6 or 7)
                {
                    plateaus[i % 2] = device.Counters.LiveResources;
                }
                else if (i > 7)
                {
                    Assert.Equal(plateaus[i % 2], device.Counters.LiveResources);
                }
            }
        }
        finally
        {
            fixture.Dispose();
        }

        Assert.Equal(0, device.Counters.LiveResources);
    }

    /// <summary>Two identical device allocation histories consume different actual rectangle payload bytes after content replacement.</summary>
    [Fact]
    public void ChangedContentChangesConsumedPayload()
    {
        using var unchanged = new RetainedSceneFixture(64);
        using var changed = new RetainedSceneFixture(64);
        changed.EditAndPublish(SceneMutation.Content);
        Assert.NotEqual(unchanged.PrepareRecordRetire(), changed.PrepareRecordRetire());
        Assert.Equal(unchanged.Device.Counters.UploadedBytes, changed.Device.Counters.UploadedBytes);
        Assert.Equal(unchanged.Device.Counters.Draws, changed.Device.Counters.Draws);
    }

    /// <summary>Each additional public primitive records real draw work and uploads, remains bounded, and releases borrowed resources.</summary>
    /// <param name="kind">The real default renderer command kind.</param>
    [Theory]
    [InlineData(PrimitiveKind.RoundedRectangle)]
    [InlineData(PrimitiveKind.Circle)]
    [InlineData(PrimitiveKind.Line)]
    [InlineData(PrimitiveKind.StrokedPath)]
    [InlineData(PrimitiveKind.Image)]
    [InlineData(PrimitiveKind.CoverageMask)]
    [InlineData(PrimitiveKind.DistanceField)]
    [InlineData(PrimitiveKind.Mesh)]
    [InlineData(PrimitiveKind.PaintGraph)]
    public void DirectPrimitiveRecordsAndReleases(PrimitiveKind kind)
    {
        var fixture = new PrimitiveCpuFixture(kind);
        CpuGraphicsDevice device = fixture.Device;
        try
        {
            Assert.Equal(64, fixture.CommandCount);
            Assert.Equal(80, device.Counters.UploadedBytes);
            Assert.Equal(2, device.Counters.Copies);
            Assert.Equal(2, device.Counters.Submissions);
            for (int prime = 0; prime < 4; prime++)
            {
                fixture.PrepareRecordDispose();
            }

            long live = device.Counters.LiveResources;
            for (int wave = 0; wave < 8; wave++)
            {
                Assert.NotEqual(0UL, fixture.PrepareRecordDispose());
                Assert.Equal(1, device.Counters.Draws);
                Assert.Equal(64, device.DrawnInstances);
                Assert.True(device.Counters.UploadedBytes > 0);
                Assert.Equal(0, device.Counters.Submissions);
                Assert.Equal(0, device.Counters.Dispatches);
                Assert.Equal(0, device.Counters.ShaderCreates);
                Assert.Equal(0, device.Counters.PipelineCreates);
                Assert.Equal(live, device.Counters.LiveResources);
            }
        }
        finally
        {
            fixture.Dispose();
        }

        Assert.Equal(0, device.Counters.LiveResources);
    }
}
