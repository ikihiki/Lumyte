using System.Collections;
using System.Reflection;
using Lumyte.Graphics.Abstractions;
using Lumyte.Mathematics.Colors;
using Xunit;
using static Lumyte.Graphics.Drawing2D.Pipelines.DrawingCompose;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks actual sparse owners against the common lazy working-budget contract.</summary>
public sealed partial class CompositionMigrationTests
{
    /// <summary>A sparse processor opts in after an inert argument-plan fork without losing prior owners.</summary>
    /// <returns>The completed no-native-allocation check.</returns>
    [Fact]
    public async Task SparseLateOptInPreservesLazyPriorOwnerAcrossArgumentForkAsync()
    {
        var device = new ProfilingDevice();
        using var resources = new DrawingResourceState2D(device);
        using var arguments = new DrawingArgumentPlan2D(device);
        using var replacement = new DrawingArgumentPlan2D(device);
        var target = new DrawingTargetInfo2D { Description = new() { Width = 8, Height = 8, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment } };
        var parent = new RenderPreparationContext2D(device, target, resources, arguments) { MaxWorkingBytes = 200000 };
        int scans = 0;
        parent.SetWorkingBytesSource(() =>
        {
            scans++;
            return 199000;
        });
        RenderPreparationContext2D fork = parent.WithArgumentPlan(replacement);
        Assert.Equal(0, scans);
        RenderPreparationContext2D capped = fork.WithWorkingLimit(300000);
        Assert.Equal(200000, capped.MaxWorkingBytes);
        Assert.Equal(0, scans);
        using var service = new SparseCpuService2D(CpuSparse(0, 0, 65536, 262144));
        var element = new SparseFillRectElement2D(service);
        FillRectCommand2D command = Assert.IsType<FillRectCommand2D>(SparseBoundaryContent(false).Commands[0]);
        ReadOnlyMemory<FillRectCommand2D> commands = new FillRectCommand2D[] { command };
        Assert.True(element.CanPrepare(fork, commands));
        Assert.Equal(0, scans);
        Assert.False(replacement.WorkingReferencesEnabled);
        await Assert.ThrowsAsync<NotSupportedException>(() => element.PrepareAsync(fork, commands).AsTask());
        Assert.Equal(1, scans);
        Assert.True(replacement.WorkingReferencesEnabled);
        Assert.Equal(0, device.NativeBuffers);
        Assert.Equal(0, device.ShaderCreations);
        Assert.Equal(0, device.ActiveTables);
    }

    /// <summary>Solid and sampled sparse pages retain all seven streams plus defaults and generation allowance.</summary>
    /// <param name="image">Whether an additional borrowed texture and sampler are present.</param>
    /// <returns>The completed fake-device packing and immutable-scalar check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SparseWorkingReferencesIncludeAllModeThreeStreamsAndUploadScalarAsync(bool image)
    {
        var device = new ProfilingDevice();
        using var resources = new DrawingResourceState2D(device);
        using var arguments = new DrawingArgumentPlan2D(device);
        var target = new DrawingTargetInfo2D { Description = new() { Width = 8, Height = 8, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment } };
        var context = new RenderPreparationContext2D(device, target, resources, arguments);
        using var texture = new DispatchTexture { Format = TextureFormat.R8Unorm };
        using IGraphicsTextureView view = texture.CreateView();
        using var sampler = new DispatchSampler();
        var canvas = new Canvas2D();
        canvas.Begin();
        if (image)
        {
            canvas.DrawCoverageMask(new(view, sampler), new(0, 0, 8, 8), new(0, 0, 8, 8), PaintGraph2D.Solid(SRgba<float>.Red));
        }
        else
        {
            canvas.FillRect(new(0, 0, 8, 8), SRgba<float>.Red);
        }

        SparseStripPlan2D strips = SparseStripBuilder2D.Create(canvas.End(), target);
        using PreparedSparseStripBatch2D batch = await PreparedSparseStripBatch2D.PrepareAsync(context, strips, default);
        Assert.True(((IPreparedWorkingBudget2D)batch).EnforcesWorkingBudget);
        Assert.True(arguments.WorkingReferencesEnabled);
        long referenceBytes = (image ? 13L : 11L) * 256;
        Assert.Equal(256 + referenceBytes, arguments.EstimatedBytes);
        object upload = SparseUploadEntry(arguments);
        Assert.Equal(referenceBytes, SparseUploadReferenceScalar(upload));
        long collecting = arguments.EstimatedBytes;
        await arguments.SealAsync();
        Assert.True(collecting >= arguments.EstimatedBytes);
        Assert.Equal(referenceBytes, SparseUploadReferenceScalar(upload));
    }

    /// <summary>Many real sparse scopes reserve complete reference generations before final packing.</summary>
    /// <returns>The completed collecting/materialized comparison.</returns>
    [Fact]
    public async Task SparseOneHundredTwentyEightGroupsReserveCompleteGenerationsAsync()
    {
        var device = new ProfilingDevice();
        using var resources = new DrawingResourceState2D(device);
        using var arguments = new DrawingArgumentPlan2D(device);
        var target = new DrawingTargetInfo2D { Description = new() { Width = 8, Height = 8, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment } };
        var context = new RenderPreparationContext2D(device, target, resources, arguments) { MaxWorkingBytes = 32 * 1024 * 1024 };
        var batches = new List<IPreparedCommandBatch2D>();
        try
        {
            for (int i = 0; i < 128; i++)
            {
                context.SetWorkingBytesSource(() => DrawingWorkingBudget2D.OwnedBytes(batches, arguments));
                SparseStripPlan2D strips = SparseStripBuilder2D.Create(SparseBoundaryContent(false).Commands[0], target);
                batches.Add(await PreparedSparseStripBatch2D.PrepareAsync(context, strips, default));
            }

            long collecting = arguments.EstimatedBytes;
            Assert.Equal(256 + (128L * 11 * 256), collecting);
            await arguments.SealAsync();
            Assert.True(collecting >= arguments.EstimatedBytes);
            Assert.Equal(128, arguments.Reservations.Count);
        }
        finally
        {
            foreach (IPreparedCommandBatch2D batch in batches.AsEnumerable().Reverse())
            {
                batch.Dispose();
            }
        }

        Assert.Empty(arguments.Reservations);
    }

    private static object SparseUploadEntry(DrawingArgumentPlan2D arguments)
    {
        FieldInfo uploads = typeof(DrawingArgumentPlan2D).GetField("_uploads", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return Assert.Single(((IEnumerable)uploads.GetValue(arguments)!).Cast<object>());
    }

    private static long SparseUploadReferenceScalar(object upload)
    {
        PropertyInfo scalar = upload.GetType().GetProperty("WorkingReferenceBytes", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return Assert.IsType<long>(scalar.GetValue(upload));
    }
}
