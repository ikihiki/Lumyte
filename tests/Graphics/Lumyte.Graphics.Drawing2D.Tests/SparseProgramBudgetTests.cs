using System.Numerics;
using System.Reflection;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Compiled paint capacity follows actual program owners, not source or page aliases.</summary>
public sealed class SparseProgramBudgetTests
{
    private const long Limit = 16 * 1024 * 1024;
    private static readonly DrawingTargetInfo2D _target = new()
    {
        Description = new() { Width = 1, Height = 1, Format = Abstractions.TextureFormat.Rgba16Float, Usage = Abstractions.TextureUsage.RenderAttachment },
    };

    /// <summary>Cold and cached shapes reject before another host program can exceed capacity.</summary>
    /// <param name="warm">Whether the shape arrays have already been cached.</param>
    /// <returns>The completed public preparation rejection.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DistinctMeshProgramsReserveBeforeCompilationAsync(bool warm)
    {
        DrawList2D content = Content(false);
        var cache = new DrawingCpuShapeCache2D(Limit, 4);
        if (warm)
        {
            cache.GetSparse(content, _target, Limit);
        }

        SparseStripPlan2D strips = cache.GetSparse(content, _target, Limit);
        Assert.Equal(1, cache.SparseBuildCount);
        var device = new ProfilingDevice();
        using var resources = new DrawingResourceState2D(device);
        using var arguments = new DrawingArgumentPlan2D(device);
        var context = new RenderPreparationContext2D(device, _target, resources, arguments) { MaxWorkingBytes = Limit };
        using PreparedSparseStripBatch2D batch = NewBatch(context, strips.WorkingBytes);
        MethodInfo build = typeof(PreparedSparseStripBatch2D).GetMethod("Build", BindingFlags.NonPublic | BindingFlags.Instance)!;
        TargetInvocationException error = Assert.Throws<TargetInvocationException>(() => build.Invoke(batch, [strips, CancellationToken.None]));
        Assert.IsType<NotSupportedException>(error.InnerException);
        List<DrawingProgram2D> programs = Field<List<DrawingProgram2D>>(batch, "_programs");
        long capacity = DrawingWorkingBudget2D.Validate<DrawingCommand2D>(new DrawingCommand2D[] { content.Commands[0] }, _target, Limit);
        Assert.NotEmpty(programs);
        Assert.Equal((Limit - strips.WorkingBytes) / capacity, programs.Count);
        Assert.Equal(strips.WorkingBytes + (programs.Count * capacity), Field<long>(batch, "_bytes"));
        Assert.True(programs.Sum(program => program.Data.Count * 16L) < Limit);
        Assert.All(programs, program => Assert.True(program.Data.Count >= 65536));
        Assert.Equal(0, device.NativeBuffers);
        using var renderer = new SparseStripRenderer2D(device, new() { MaxWorkingBytes = Limit });
        for (int attempt = 0; attempt < (warm ? 2 : 1); attempt++)
        {
            using var publicArguments = new DrawingArgumentPlan2D(device);
            await Assert.ThrowsAsync<NotSupportedException>(async () =>
                await ((IDrawingPassRenderer2D)renderer).PreparePassesAsync(content, _target, publicArguments, default));
            Assert.Equal(0, device.NativeBuffers);
        }
    }

    /// <summary>Genuine command dictionary hits retain one program reservation, even across warm shapes.</summary>
    /// <returns>The completed cold and warm preparations.</returns>
    [Fact]
    public async Task SharedMeshProgramIsReservedOnceAsync()
    {
        DrawList2D content = Content(true);
        var device = new ProfilingDevice();
        using var resources = new DrawingResourceState2D(device);
        var cache = new DrawingCpuShapeCache2D(Limit, 4);
        for (int frame = 0; frame < 2; frame++)
        {
            using var arguments = new DrawingArgumentPlan2D(device);
            var context = new RenderPreparationContext2D(device, _target, resources, arguments) { MaxWorkingBytes = Limit, CpuShapes = cache };
            using PreparedSparseStripBatch2D batch = await PreparedSparseStripBatch2D.PrepareAsync(context, content, default);
            Assert.Single(Field<List<DrawingProgram2D>>(batch, "_programs"));
            long capacity = DrawingWorkingBudget2D.Validate<DrawingCommand2D>(new DrawingCommand2D[] { content.Commands[0] }, _target, Limit);
            DrawRecordPage2D page = Field<DrawRecordPage2D>(batch, "_page");
            long pageBytes = (page.Records.Count * 4096L) + (page.Numeric.Count * 64L) + (page.Paints.Count * 4096L);
            Assert.Equal(cache.GetSparse(content, _target, Limit).WorkingBytes + capacity + pageBytes, Field<long>(batch, "_bytes"));
            var owners = new Dictionary<object, long>(ReferenceEqualityComparer.Instance);
            batch.AccountMemory(owners);
            Assert.Equal(batch.EstimatedBytes, owners.Values.Sum());
        }

        Assert.Equal(1, cache.SparseBuildCount);
    }

    private static DrawList2D Content(bool shared)
    {
        var paint = PaintGraph2D.LinearGradient(
            Vector2.Zero,
            Vector2.UnitX,
            Enumerable.Range(0, 32768).Select(i => new ColorStop2D(i / 32767f, new(1, 0, 0, 1))).ToArray());
        var state = new DrawingState2D(Matrix3x2.Identity, ClipStack2D.Empty);
        Mesh2D Mesh() => new([new(new(-1, -1), 1), new(new(4, -1), 1), new(new(-1, 4), 1)], [0, 1, 2]);
        Mesh2D mesh = Mesh();
        return new(Enumerable.Range(0, 64).Select(_ => new DrawMeshCommand2D(shared ? mesh : Mesh(), paint, state)));
    }

    // Inspect the failed owner directly: no GC timing or allocation-size heuristic is an oracle.
    private static PreparedSparseStripBatch2D NewBatch(RenderPreparationContext2D context, long bytes)
    {
        var batch = (PreparedSparseStripBatch2D)Activator.CreateInstance(typeof(PreparedSparseStripBatch2D), BindingFlags.NonPublic | BindingFlags.Instance, null, [context], null)!;
        typeof(PreparedSparseStripBatch2D).GetField("_bytes", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(batch, bytes);
        return batch;
    }

    private static T Field<T>(PreparedSparseStripBatch2D batch, string name) =>
        (T)typeof(PreparedSparseStripBatch2D).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(batch)!;
}
