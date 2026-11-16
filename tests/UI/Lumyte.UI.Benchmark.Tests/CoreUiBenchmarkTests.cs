using Lumyte.Graphics.Drawing2D;
using Xunit;

namespace Lumyte.UI.Benchmark.Tests;

/// <summary>Deterministic public-API workload contracts, without timing or GC budgets.</summary>
public sealed class CoreUiBenchmarkTests
{
    /// <summary>Passive declarations create independent finite mounted instances.</summary>
    /// <param name="count">Actual leaf count.</param>
    [Theory]
    [InlineData(16)]
    [InlineData(128)]
    public void DeclarationMountLifetimesAreIndependent(int count)
    {
        using var workload = new UiTreeWorkload(count);
        Compose.Definitions.Widget declaration = workload.Declare();
        using var first = new UiSurface(declaration);
        using var second = new UiSurface(declaration);
        Assert.Equal(count + 2, UiTreeWorkload.CountElements(first.RootElement));
        Assert.NotSame(first.RootElement, second.RootElement);
        UiElement retained = first.RootElement.Children[1];
        first.Dispose();
        Assert.False(retained.IsMounted);
        Assert.True(second.RootElement.Children[1].IsMounted);
    }

    /// <summary>Stable-key reorder retains exact elements; structural replacement unmounts them.</summary>
    /// <param name="count">Actual leaf count.</param>
    [Theory]
    [InlineData(16)]
    [InlineData(128)]
    public void ReconciliationRetainsOrReplacesExactIdentities(int count)
    {
        using var workload = new UiTreeWorkload(count);
        UiElement[] original = workload.Surface.RootElement.Children.ToArray();
        workload.Reconcile(false);
        Assert.Equal(original.Reverse(), workload.Surface.RootElement.Children);
        workload.Reconcile(false);
        Assert.Equal(original, workload.Surface.RootElement.Children);
        workload.Reconcile(true);
        Assert.All(original, element => Assert.False(element.IsMounted));
        for (int i = 0; i < 20; i++)
        {
            workload.Reconcile(true);
            Assert.Equal(count + 2, UiTreeWorkload.CountElements(workload.Surface.RootElement));
        }
    }

    /// <summary>One actual signal rebuilds only its function and preserves sibling identities.</summary>
    /// <param name="count">Actual leaf count.</param>
    [Theory]
    [InlineData(16)]
    [InlineData(128)]
    public void SignalUpdateHasExactBuildAndBoundsContract(int count)
    {
        using var workload = new UiTreeWorkload(count);
        UiElement sibling = workload.Surface.RootElement.Children[1];
        UiElement function = workload.Surface.RootElement.Children[0];
        UiFunctionElement runtime = Assert.IsAssignableFrom<UiFunctionElement>(function);
        Assert.Equal(1, workload.Builds);
        workload.LayoutLeaf();
        Assert.Equal(2, workload.Builds);
        Assert.Same(function, workload.Surface.RootElement.Children[0]);
        Assert.Same(sibling, workload.Surface.RootElement.Children[1]);
        Assert.Equal(new Rect2D(0, 0, 12, 8), runtime.Content.Bounds);
        Assert.Equal(new Rect2D(0, 8, 10, 8), sibling.Bounds);
        workload.LayoutLeaf();
        Assert.Equal(3, workload.Builds);
        Assert.Equal(10, runtime.Content.Size.X);
    }

    /// <summary>Both shallow and bounded deep fixtures update real viewport geometry.</summary>
    /// <param name="depth">Actual wrapper depth.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(24)]
    public void LayoutDepthAndViewportAreReal(int depth)
    {
        using var workload = new UiTreeWorkload(32, depth);
        Assert.Equal(34 + depth, UiTreeWorkload.CountElements(workload.Surface.RootElement));
        workload.LayoutViewport();
        Assert.Equal(514, workload.Surface.RootElement.Size.X);
        workload.LayoutViewport();
        Assert.Equal(512, workload.Surface.RootElement.Size.X);
        workload.LayoutLeaf();
        Assert.Equal(2, workload.Builds);
    }

    /// <summary>Local recordings are CPU captures and clean siblings are reused exactly.</summary>
    /// <param name="count">Actual leaf count.</param>
    [Theory]
    [InlineData(16)]
    [InlineData(128)]
    public void LocalCaptureAndSnapshotLifetimesAreExact(int count)
    {
        using var workload = new UiDrawingWorkload(count);
        Assert.Equal(count + 1, workload.Drawing.LastRecordingCount);
        workload.Drawing.Update();
        Assert.Equal(0, workload.Drawing.LastRecordingCount);
        workload.Update(false);
        Assert.Equal(1, workload.Drawing.LastRecordingCount);
        workload.Update(true);
        Assert.Equal(count, workload.Drawing.LastRecordingCount);
        using DrawingSnapshot2D snapshot = workload.Drawing.Scene.CaptureSnapshot();
        ulong revision = snapshot.Revision;
        using (DrawingSceneReadLease2D borrow = workload.Drawing.Scene.Borrow())
        {
            Assert.Throws<InvalidOperationException>(workload.Drawing.Update);
        }

        workload.Update(false);
        Assert.Equal(revision, snapshot.Revision);
        Assert.NotEqual(revision, workload.Snapshot());
    }

    /// <summary>Prepared input routes actual capture, activation and advancing focus.</summary>
    [Fact]
    public void InputPhasesHaveIndependentObservableEffects()
    {
        var benchmark = new InputBenchmarks();
        benchmark.Setup();
        try
        {
            Assert.True(benchmark.HitTest());
            Assert.True(benchmark.HitTest());
            Assert.Equal(0, benchmark.Clicks);
            Assert.True(benchmark.CaptureClick());
            Assert.Equal(1, benchmark.Clicks);
            UiElement? first = benchmark.Focus();
            UiElement? second = benchmark.Focus();
            Assert.NotNull(first);
            Assert.NotSame(first, second);
            Assert.Equal(17, UiTreeWorkload.CountElements(benchmark.Surface.RootElement));
        }
        finally
        {
            benchmark.Cleanup();
        }
    }

    /// <summary>Managed submission counts every rendered instance and releases all resources.</summary>
    /// <param name="count">Actual leaf count.</param>
    /// <returns>Managed simulation verification completion.</returns>
    [Theory]
    [InlineData(16)]
    [InlineData(128)]
    public async Task RendererSimulationRetiresOwnersAndPlateausAsync(int count)
    {
        var workload = new UiDrawingWorkload(count);
        try
        {
            await workload.RetainedSimulatedAsync();
            Assert.Equal(count, workload.Device.DrawnInstances);
            Assert.Equal(1, workload.Device.Counters.Submissions);
            Assert.Equal(0, workload.Retention.ActiveFrames);
            await workload.RetainedSimulatedAsync();
            await workload.RetainedSimulatedAsync();
            await workload.RetainedSimulatedAsync();
            long live = workload.Device.Counters.LiveResources;
            int assets = workload.Retention.CachedAssets;
            long copies = workload.Device.Counters.Copies;
            long reuse = workload.Retention.ReusedRuns;
            long compiled = workload.Retention.CompiledRuns;
            for (int i = 0; i < 6; i++)
            {
                await workload.RetainedSimulatedAsync();
                Assert.Equal(live, workload.Device.Counters.LiveResources);
                Assert.Equal(assets, workload.Retention.CachedAssets);
                Assert.Equal(0, workload.Retention.ActiveFrames);
            }

            Assert.Equal(reuse + (6 * (count - 1)), workload.Retention.ReusedRuns);
            Assert.Equal(compiled + 6, workload.Retention.CompiledRuns);
            Assert.Equal(copies + 12, workload.Device.Counters.Copies);
            Assert.Equal(count * 10, workload.Device.DrawnInstances);
            await workload.StandardSimulatedAsync();
            Assert.Equal(count * 11, workload.Device.DrawnInstances);
            Assert.Equal(11, workload.Device.Counters.Submissions);
        }
        finally
        {
            workload.Dispose();
        }

        Assert.Equal(0, workload.Device.Counters.LiveResources);
    }
}
