using Lumyte.Graphics.Benchmark.Integration;
using Lumyte.Maps;
using Xunit;
using Fixture = Lumyte.Graphics.Drawing2D.Benchmark.Maps.MapCpuBenchmarks;

namespace Lumyte.Graphics.Benchmark.Tests;

/// <summary>Deterministic map source, recording and completion-safe GPU ownership tests.</summary>
public sealed class MapWorkloadTests
{
    /// <summary>Executes the deterministic map workload contract.</summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CpuCasesExecuteRealContentAndMovingInputsAsync()
    {
        var cases = new Fixture();
        await cases.SetupAsync();
        try
        {
            MapTileContent decoded = cases.MvtDecoderDecode();
            Assert.Equal(3, decoded.Layers.Length);
            Assert.Equal(289, decoded.Layers.Sum(layer => layer.Features.Length));
            Assert.All(decoded.Layers, layer => Assert.Equal(4096U, layer.Extent));
            Assert.Equal(new[] { "water", "buildings", "roads" }, decoded.Layers.Select(layer => layer.Name));
            Assert.Equal(new MapPoint(64, 64), decoded.Layers[1].Features[0].Paths[0].Points[0]);
            Assert.Equal(new MapPoint(224, 224), decoded.Layers[1].Features[0].Paths[0].Points[2]);
            Assert.Equal(new MapPoint(4096, 3938), decoded.Layers[2].Features[31].Paths[0].Points[1]);
            Assert.True(await cases.SourceAcquireAsync() > 0);
            Assert.True(cases.StaticSessionRecord().Count > 289);
            Assert.True(cases.FixedPanContinuousZoomRecord().Count > 289);
            MapCamera first = cases.Camera;
            for (int frame = 1; frame <= 112; frame++)
            {
                cases.FixedPanContinuousZoomRecord();
            }

            Assert.Equal(first, cases.Camera);
            Assert.Equal("4BD7A92AFB49166C49E130A52C08F4B6BA403ED6B3E134B7B280B90D1A6255AD", cases.FixtureHash);
            Assert.Equal(cases.FixtureHash, decoded.SourceRevision);
            Assert.NotEqual(0UL, cases.WarmCpuPrepareCommandRecordDispose());
            Assert.True(cases.Counters.Draws > 0);
            Assert.True(cases.Counters.UploadedBytes > 0);
            Assert.Equal(0, cases.Counters.Submissions);
        }
        finally
        {
            await cases.CleanupAsync();
        }
    }

    /// <summary>Checks genuine M4 spatial arrivals, publication, held retirement and actual manager trim.</summary>
    /// <returns>Completion of the deterministic A-B-A ownership assertions.</returns>
    [Fact]
    public async Task ControlledStreamingUsesActualDemandRetirementAndManagerTrimAsync()
    {
        for (int repeat = 0; repeat < 2; repeat++)
        {
            Lumyte.Graphics.Drawing2D.Benchmark.Maps.StreamingObservation observed =
                await Lumyte.Graphics.Drawing2D.Benchmark.Maps.MapStreamingWorkload.RunAsync();
            Assert.Equal(3, observed.Builds);
            Assert.Equal(3, observed.Publications);
            Assert.Equal(2, observed.HeldRetirements);
            Assert.Equal(2, observed.Releases);
            Assert.Equal(2UL, observed.ManagerReleases);
        }
    }

    /// <summary>Checks real MapView recording reuse and M3 invalidation without a GPU.</summary>
    /// <returns>Completion of borrowed-session correctness checks.</returns>
    [Fact]
    public async Task MapViewRetainsStaticAndInvalidatesMovingContentAsync()
    {
        await using var workload = new Lumyte.Benchmarks.Maps.MapWorkload();
        await workload.WarmAsync();
        using var surface = new Lumyte.UI.UiSurface(new Lumyte.Maps.UI.MapView { Session = workload.Session });
        surface.SetViewport(new(512, 512));
        using Lumyte.UI.UiDrawingScene2D scene = surface.CreateDrawingScene();
        scene.Update();
        Assert.Equal(1, scene.LastRecordingCount);
        scene.Update();
        Assert.Equal(0, scene.LastRecordingCount);
        workload.Move(5);
        scene.Update();
        Assert.Equal(1, scene.LastRecordingCount);
        scene.Update();
        Assert.Equal(0, scene.LastRecordingCount);
        Assert.Equal(1, workload.Session.PublishedTiles);
    }

    /// <summary>Executes the deterministic map workload contract.</summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [GpuIntegratedBenchmarkTests.IntegrationGpuFact]
    public async Task MapSlotsDrainAndPassIndependentOracleAsync()
    {
        foreach (bool retained in new[] { false, true })
        {
            foreach (bool moving in new[] { false, true })
            {
                foreach (int depth in new[] { 1, 3 })
                {
                    await using var workload = new MapFrameWorkload(retained, moving, depth);
                    for (int repeat = 0; repeat < 2; repeat++)
                    {
                        await workload.RunAsync();
                        Assert.Equal(6, workload.CompletedFrames);
                        Assert.Equal(5, workload.LastCompletedFrame);
                        Assert.Equal(depth, workload.PeakOutstanding);
                        await workload.VerifyLastFrameAsync();
                    }
                }
            }
        }
    }
}
