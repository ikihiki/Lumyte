using Lumyte.Graphics.Abstractions;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks each retained record payload keeps its own immutable prior-owner prefix.</summary>
public sealed class RetainedPriorOwnerSnapshotTests
{
    /// <summary>Large rectangles then independent small circles do not charge the first payload twice at upload.</summary>
    /// <returns>The asynchronous normal resident preparation verification.</returns>
    [Fact]
    public async Task MixedResidentPayloadsKeepTheirOriginalPriorOwnerChargeAsync()
    {
        const long Maximum = 3190000;
        var device = new ProfilingDevice();
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault(), retentionOptions: new() { EnableResidentInstances = true, MaxWorkingBytes = Maximum });
        using var target = new DispatchTexture { Width = 64, Height = 64 };
        using IGraphicsTextureView view = target.CreateView();
        using var scene = new DrawingScene2D();
        scene.Insert(scene.Root, 0, Rectangles());
        for (int i = 0; i < 400; i++)
        {
            scene.Insert(scene.Root, i + 1, Circle());
        }

        using DrawingSnapshot2D snapshot = scene.CaptureSnapshot();
        long capture = ResidentSceneBudget2D.ValidateCapture(snapshot, Maximum);
        Assert.Equal(1431552, capture);
        Console.WriteLine($"mixedBudget Maximum={Maximum} capture={capture} commands=528 groups=401 large=128 small=400");
        using PreparedDrawingFrame2D prepared = await renderer.PrepareAsync(snapshot, new() { View = view });
        Assert.Equal(401, renderer.RetentionStatistics.CompiledRuns);
        using var commands = new ProfilingCommands();
        prepared.Record(commands);
        commands.Dispose();
        prepared.AbortUnsubmitted();
        Assert.Equal(528, device.Records.Count);
    }

    /// <summary>The same unmodified scene still rejects a genuinely insufficient preallocation cap before buffers exist.</summary>
    /// <returns>The asynchronous conservative rejection verification.</returns>
    [Fact]
    public async Task SameMixedSceneRejectsInsufficientBudgetBeforePhysicalAllocationAsync()
    {
        var device = new ProfilingDevice();
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault(), retentionOptions: new() { EnableResidentInstances = true, MaxWorkingBytes = 3000000 });
        using var target = new DispatchTexture { Width = 64, Height = 64 };
        using IGraphicsTextureView view = target.CreateView();
        using var scene = new DrawingScene2D();
        scene.Insert(scene.Root, 0, Rectangles());
        for (int i = 0; i < 400; i++)
        {
            scene.Insert(scene.Root, i + 1, Circle());
        }

        using DrawingSnapshot2D snapshot = scene.CaptureSnapshot();
        await Assert.ThrowsAsync<NotSupportedException>(async () => await renderer.PrepareAsync(snapshot, new() { View = view }));
        Assert.Equal(0, device.NativeBuffers);
        Assert.Equal(0, renderer.RetentionStatistics.ActiveFrames);
        Assert.Equal(0, renderer.RetentionStatistics.CachedAssets);
    }

    /// <summary>Snapshot scalars remain independent while late reads still share origin-owned callback invalidation.</summary>
    /// <returns>The asynchronous scalar and callback-scope verification.</returns>
    [Fact]
    public async Task SnapshotKeepsScalarAndLazyOriginScopeWithoutFreezingOwnPlanAsync()
    {
        var device = new ProfilingDevice();
        using var resources = new DrawingResourceState2D(device);
        using var plan = new DrawingArgumentPlan2D(device);
        using IDisposable scope = plan.HoldWorkingBytesSources();
        var info = new DrawingTargetInfo2D { Description = new() { Width = 64, Height = 64, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment }, PixelScale = 1 };
        var origin = new RenderPreparationContext2D(device, info, resources, plan) { EnforceWorkingBudget = true, MaxWorkingBytes = 1000, OtherWorkingBytes = 200 };
        RenderPreparationContext2D frozen = origin.SnapshotWorkingBudget();
        origin.OtherWorkingBytes = 500;
        Assert.Equal(200, frozen.OtherWorkingBytes);
        Assert.Same(origin.Clips, frozen.Clips);
        Assert.Same(plan, frozen.ArgumentPlan);
        long previousPlanBytes = plan.EstimatedBytes;
        plan.EnableWorkingReferenceBudget();
        ShaderResources reservation = plan.Reserve();
        plan.ReserveWorkingReferences(reservation, 2, 0, 0);
        Assert.True(frozen.ArgumentPlan.EstimatedBytes > previousPlanBytes);
        int reads = 0;
        long lateBytes = 10;
        origin.SetWorkingBytesSource(() =>
        {
            reads++;
            return lateBytes;
        });
        RenderPreparationContext2D late = origin.SnapshotWorkingBudget().WithWorkingLimit(2000);
        Assert.Equal(0, reads);
        await Task.Yield();
        lateBytes = 700;
        Assert.Equal(700, late.OtherWorkingBytes);
        Assert.Equal(1, reads);
        Assert.Equal(1000, late.MaxWorkingBytes);
        origin.SetWorkingBytesSource(() => throw new InvalidOperationException("An unread callback must not run during invalidation."));
        RenderPreparationContext2D unread = origin.SnapshotWorkingBudget();
        Assert.True(unread.HasWorkingBytesSource);
        scope.Dispose();
        Assert.False(unread.HasWorkingBytesSource);
        Assert.False(origin.HasWorkingBytesSource);
        Assert.Equal(700, late.OtherWorkingBytes);
        Assert.Equal(1, reads);
    }

    private static DrawList2D Rectangles()
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        for (int i = 0; i < 128; i++)
        {
            canvas.FillRect(new(2, 2, 4, 4), new SRgba<float>(1, 0, 0, 1));
        }

        return canvas.End();
    }

    private static DrawList2D Circle()
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillCircle(new(8, 8), 2, new SRgba<float>(0, 1, 0, 1));
        return canvas.End();
    }
}
