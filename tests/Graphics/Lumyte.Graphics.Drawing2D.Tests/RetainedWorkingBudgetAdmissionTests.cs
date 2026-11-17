using Lumyte.Graphics.Abstractions;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks that cumulative preparation accounting runs only for an enforced working budget.</summary>
public sealed class RetainedWorkingBudgetAdmissionTests
{
    /// <summary>Unbudgeted cold entries collect their stable owners once; budgeted entries retain cumulative accounting.</summary>
    /// <param name="enabled">Null selects the unchanged constructor default.</param>
    /// <param name="entries">The independent cold placement count.</param>
    /// <returns>The asynchronous preparation check.</returns>
    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 8)]
    [InlineData(false, 32)]
    [InlineData(null, 32)]
    [InlineData(true, 8)]
    public async Task ColdEntriesOnlyScanCumulativeOwnersWhenBudgetEnforcedAsync(bool? enabled, int entries)
    {
        var callback = new CountedRenderer();
        var device = new ProfilingDevice();
        using Renderer2D renderer = CreateRenderer(device, callback, enabled);
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        using var scene = new DrawingScene2D();
        DrawList2D content = Content();
        for (int i = 0; i < entries; i++)
        {
            scene.Insert(scene.Root, i, content);
        }

        using DrawingSnapshot2D snapshot = scene.CaptureSnapshot();
        using PreparedDrawingFrame2D frame = await renderer.PrepareAsync(snapshot, new() { View = view });
        Assert.Equal(entries, callback.Prepares);
        Assert.All(callback.BudgetEnforcement, enforced => Assert.Equal(enabled == true, enforced));
        if (enabled == true)
        {
            Assert.True(callback.OwnerReads > entries);
            Assert.True(callback.OtherWorkingBytes[^1] > callback.OtherWorkingBytes[0]);
        }
        else
        {
            Assert.Equal(entries, callback.OwnerReads);
            Assert.All(callback.OtherWorkingBytes, bytes => Assert.Equal(0, bytes));
        }

        frame.Dispose();
        Assert.Equal(entries, callback.Disposals);
        Assert.Equal(0, renderer.RetentionStatistics.ActiveFrames);
    }

    /// <summary>Enforced custom work still includes earlier owners and rejects the cumulative frame.</summary>
    /// <returns>The asynchronous rejection and cleanup check.</returns>
    [Fact]
    public async Task EnforcedCustomBatchesKeepCumulativeChargeAndCleanupAsync()
    {
        var callback = new CountedRenderer { BytesPerBatch = 700000 };
        var device = new ProfilingDevice();
        using Renderer2D renderer = CreateRenderer(device, callback, true, 1000000);
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        using var scene = new DrawingScene2D();
        scene.Insert(scene.Root, 0, Content());
        scene.Insert(scene.Root, 1, Content());
        using DrawingSnapshot2D snapshot = scene.CaptureSnapshot();
        await Assert.ThrowsAsync<NotSupportedException>(async () => await renderer.PrepareAsync(snapshot, new() { View = view }));
        Assert.Equal(2, callback.Prepares);
        Assert.All(callback.BudgetEnforcement, enforced => Assert.True(enforced));
        Assert.True(callback.OtherWorkingBytes[1] >= callback.BytesPerBatch);
        Assert.Equal(2, callback.Disposals);
        Assert.Equal(0, renderer.RetentionStatistics.ActiveFrames);
        Assert.Equal(0, renderer.RetentionStatistics.CachedAssets);
    }

    private static Renderer2D CreateRenderer(ProfilingDevice device, CountedRenderer callback, bool? enabled, long maximum = 256 * 1024 * 1024)
    {
        var defaults = DrawingCommandRenderers2D.CreateDefault();
        var table = new DrawingCommandRenderers2D(callback, defaults.FillRoundedRect, defaults.FillCircle, defaults.DrawLine, defaults.FillPath, defaults.StrokePath, defaults.DrawImage, defaults.DrawCoverageMask, defaults.DrawDistanceField, defaults.DrawMesh, defaults.DrawPaintGraph);
        return enabled is { } resident
            ? new(device, table, retentionOptions: new() { EnableResidentInstances = resident, MaxWorkingBytes = maximum })
            : new(device, table);
    }

    private static DrawList2D Content()
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new(2, 2, 4, 4), new SRgba<float>(1, 0, 0, 1));
        return canvas.End();
    }

    private sealed class CountedRenderer : IFillRectRenderer2D
    {
        internal long BytesPerBatch { get; init; } = 64;

        internal int Prepares { get; private set; }

        internal int OwnerReads { get; set; }

        internal int Disposals { get; set; }

        internal List<bool> BudgetEnforcement { get; } = [];

        internal List<long> OtherWorkingBytes { get; } = [];

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillRectCommand2D> commands, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Prepares++;
            BudgetEnforcement.Add(context.EnforceWorkingBudget);
            OtherWorkingBytes.Add(context.OtherWorkingBytes);
            return ValueTask.FromResult<IPreparedCommandBatch2D>(new CountedBatch(this));
        }
    }

    private sealed class CountedBatch(CountedRenderer owner) : IPreparedCommandBatch2D, IRetainedMemory2D
    {
        private bool _disposed;

        public bool HasStableOwners => true;

        public void AccountMemory(Dictionary<object, long> owners)
        {
            owner.OwnerReads++;
            owners.TryAdd(this, owner.BytesPerBatch);
        }

        public void DeclareResources(IDrawingResourceDeclarations2D resources)
        {
        }

        public void Record(IGraphicsCommandBuffer commands, IGraphicsTextureView target)
        {
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                owner.Disposals++;
            }
        }
    }
}
