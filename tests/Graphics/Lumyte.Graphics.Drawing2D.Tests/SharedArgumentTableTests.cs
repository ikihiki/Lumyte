using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Verifies exact logical registrations independently of physical binding limits.</summary>
public sealed class SharedArgumentTableTests
{
    /// <summary>Empty, one-slot and larger plans use only their actual distinct resources.</summary>
    /// <param name="count">The exact number of views.</param>
    /// <returns>The asynchronous test completion.</returns>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(259)]
    public async Task ExactCapacityAndSealBoundaryAsync(int count)
    {
        var device = new ProfilingDevice();
        using var plan = new DrawingArgumentPlan2D(device);
        using var texture = new DispatchTexture();
        var views = new List<IGraphicsTextureView>();
        try
        {
            for (int i = 0; i < count; i++)
            {
                IGraphicsTextureView view = texture.CreateView();
                views.Add(view);
                IGpuRef<IGraphicsTextureView>? pending = null;
                IGpuRef<IGraphicsTextureView>? duplicate = null;
                plan.RegisterTexture(view, reference => pending = reference);
                plan.RegisterTexture(view, reference => duplicate = reference);
                plan.DeferUpload(_ =>
                {
                    Assert.NotNull(pending);
                    Assert.Same(pending, duplicate);
                    return ValueTask.CompletedTask;
                });
                Assert.Null(pending);
                Assert.Null(duplicate);
            }

            await plan.SealAsync();
            Assert.Equal(count == 0 ? 0 : 1, device.Tables.Count);
            if (count != 0)
            {
                ArgumentTableDesc desc = Assert.Single(device.Tables);
                Assert.Equal((uint)count, desc.TextureCapacity);
                Assert.Equal(0u, desc.SamplerCapacity);
                Assert.Equal(0u, desc.BufferCapacity);
                Assert.Equal(count, device.Writes);
                Assert.Throws<ArgumentOutOfRangeException>(() => plan.Table.WriteTexture((uint)count, views[0]));
            }

            Assert.Throws<InvalidOperationException>(() => plan.RegisterTexture(texture.CreateView(), _ => { }));
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await plan.SealAsync());
        }
        finally
        {
            foreach (IGraphicsTextureView view in views)
            {
                view.Dispose();
            }
        }
    }

    /// <summary>Keys use resource identity, exact ranges and sampler object identity.</summary>
    /// <returns>The asynchronous test completion.</returns>
    [Fact]
    public async Task ExactIdentityAndRangeDeduplicationAsync()
    {
        var device = new ProfilingDevice();
        using var plan = new DrawingArgumentPlan2D(device);
        using var texture = new DispatchTexture();
        using IGraphicsTextureView first = texture.CreateView();
        using IGraphicsTextureView second = texture.CreateView();
        using var sampler = new DispatchSampler();
        using var equalSampler = new DispatchSampler();
        using IGraphicsBuffer<Vector4> buffer = device.CreateBuffer<Vector4>(new() { Count = 8, Usage = BufferUsage.ShaderRead });
        IGpuRef<Vector4>? range = null;
        plan.RegisterBuffer(buffer.SliceElements(0, 4), reference => range = reference);
        IGpuRef<Vector4>? duplicate = null;
        IGpuRef<Vector4>? offset = null;
        IGpuRef<Vector4>? count = null;
        IGpuRef<IGraphicsTextureView>? firstImage = null;
        IGpuRef<IGraphicsTextureView>? sameImage = null;
        IGpuRef<IGraphicsTextureView>? otherImage = null;
        IGpuRef<IGraphicsSampler>? firstSampler = null;
        IGpuRef<IGraphicsSampler>? sameSampler = null;
        IGpuRef<IGraphicsSampler>? otherSampler = null;
        plan.RegisterBuffer(buffer.SliceElements(0, 4), reference => duplicate = reference);
        plan.RegisterBuffer(buffer.SliceElements(1, 4), reference => offset = reference);
        plan.RegisterBuffer(buffer.SliceElements(0, 3), reference => count = reference);
        plan.RegisterTexture(first, reference => firstImage = reference);
        plan.RegisterTexture(first, reference => sameImage = reference);
        plan.RegisterTexture(second, reference => otherImage = reference);
        plan.RegisterSampler(sampler, reference => firstSampler = reference);
        plan.RegisterSampler(sampler, reference => sameSampler = reference);
        plan.RegisterSampler(equalSampler, reference => otherSampler = reference);
        await plan.SealAsync();
        Assert.NotNull(range);
        Assert.Same(range, duplicate);
        Assert.NotSame(range, offset);
        Assert.NotSame(range, count);
        Assert.Same(firstImage, sameImage);
        Assert.NotSame(firstImage, otherImage);
        Assert.Same(firstSampler, sameSampler);
        Assert.NotSame(firstSampler, otherSampler);
        Assert.Equal(new ArgumentTableDesc { TextureCapacity = 2, SamplerCapacity = 2, BufferCapacity = 3 }, Assert.Single(device.Tables));
        Assert.Same(plan.Table, ProfilingDevice.Owner(range!));
    }

    /// <summary>Backend failure publishes no references or uploads and retains final cleanup for retry.</summary>
    /// <returns>The asynchronous test completion.</returns>
    [Fact]
    public async Task FailedMaterializationNeverPublishesReferencesAsync()
    {
        var device = new ProfilingDevice { FailWrite = 2, FailTableDisposeOnce = true };
        var plan = new DrawingArgumentPlan2D(device);
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        using var sampler = new DispatchSampler();
        IGpuRef<IGraphicsTextureView>? image = null;
        plan.RegisterTexture(view, reference => image = reference);
        plan.RegisterSampler(sampler, _ => { });
        bool uploaded = false;
        plan.DeferUpload(_ =>
        {
            uploaded = true;
            return ValueTask.CompletedTask;
        });
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await plan.SealAsync());
        Assert.False(uploaded);
        Assert.Null(image);
        Assert.Throws<InvalidOperationException>(plan.Dispose);
        Assert.Equal(1, device.ActiveTables);
        plan.Dispose();
        plan.Dispose();
        Assert.Equal(0, device.ActiveTables);
    }

    /// <summary>Reference availability begins after all writes, while upload failure invalidates the plan.</summary>
    /// <returns>The asynchronous test completion.</returns>
    [Fact]
    public async Task FinalizerFailureInvalidatesPreviouslyMaterializedHandlesAsync()
    {
        var device = new ProfilingDevice();
        using var plan = new DrawingArgumentPlan2D(device);
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        IGpuRef<IGraphicsTextureView>? image = null;
        plan.RegisterTexture(view, reference => image = reference);
        plan.DeferUpload(_ =>
        {
            Assert.Same(plan.Table, ProfilingDevice.Owner(image!));
            Assert.Throws<InvalidOperationException>(() => plan.RegisterTexture(view, _ => { }));
            throw new OperationCanceledException();
        });
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await plan.SealAsync());
        Assert.NotNull(image);
        Assert.False(Assert.Single(plan.Reservations).IsReady);
    }

    /// <summary>Independent leases return out of order and retry the final native disposal.</summary>
    /// <returns>The asynchronous test completion.</returns>
    [Fact]
    public async Task LeasesKeepTableAliveUntilLastSuccessfulReleaseAsync()
    {
        var device = new ProfilingDevice { FailTableDisposeOnce = true };
        var plan = new DrawingArgumentPlan2D(device);
        IDisposable first = plan.Retain();
        IDisposable second = plan.Retain();
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        IGpuRef<IGraphicsTextureView>? image = null;
        plan.RegisterTexture(view, reference => image = reference);
        await plan.SealAsync();
        plan.Dispose();
        second.Dispose();
        Assert.Same(plan.Table, ProfilingDevice.Owner(image!));
        Assert.Throws<InvalidOperationException>(first.Dispose);
        Assert.Equal(1, device.ActiveTables);
        first.Dispose();
        first.Dispose();
        Assert.Equal(0, device.ActiveTables);
        Assert.False(Assert.Single(plan.Reservations).IsReady);
    }

    /// <summary>Two hundred independent clips share one table without merging buffers or draws.</summary>
    /// <param name="portable">Whether the individual portable route is selected.</param>
    /// <returns>The asynchronous test completion.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TwoHundredClippedProgramsShareOneTableAsync(bool portable)
    {
        var device = new ProfilingDevice { Caps = new() { ShaderTarget = portable ? ShaderTarget.Wgsl : ShaderTarget.SpirV, MaxBufferSize = 1UL << 30 } };
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        using var target = new DispatchTexture();
        using IGraphicsTextureView view = target.CreateView();
        var canvas = new Canvas2D();
        canvas.Begin();
        for (int i = 0; i < 200; i++)
        {
            canvas.Save();
            canvas.ClipRect(new(0, 0, 16, 16));
            canvas.FillCircle(new(8, 8), 4, new Color2D(1, 0, 0, 1));
            canvas.Restore();
        }

        using PreparedDraw2D prepared = await renderer.PrepareAsync(canvas.End(), new() { View = view });
        ArgumentTableDesc desc = Assert.Single(device.Tables);
        Assert.Equal(1u, desc.TextureCapacity);
        Assert.Equal(1u, desc.SamplerCapacity);
        Assert.Equal(7u, desc.BufferCapacity);
        Assert.Equal(14, device.NativeBuffers);
        using var commands = new ProfilingCommands();
        prepared.Record(commands);
        Assert.Equal(portable ? 200 : 1, commands.Events.OfType<DrawRangeArguments2D>().Count());
        Assert.Equal(200u, commands.Events.OfType<DrawRangeArguments2D>().Sum(arguments => arguments.RecordCount));
        IArgumentTable table = Assert.Single(commands.Events.OfType<IArgumentTable>().Distinct());
        foreach (DrawRangeArguments2D arguments in commands.Events.OfType<DrawRangeArguments2D>())
        {
            Assert.Same(table, ProfilingDevice.Owner(arguments.Records));
            Assert.Same(table, ProfilingDevice.Owner(arguments.PageMetadata));
        }

        Assert.All(device.ShaderValues.SelectMany(value => value.Values).Where(value => value.IsReference), value => Assert.Same(table, ((IShaderReference)value.Reference!).Table));
        Assert.All(device.Instructions, instruction =>
        {
            Assert.Same(table, ProfilingDevice.Owner(instruction.Texture));
            Assert.Same(table, ProfilingDevice.Owner(instruction.Sampler));
            Assert.DoesNotContain(typeof(DrawingInstruction).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance), property => property.PropertyType.Name.Contains("ArgumentRegistration", StringComparison.Ordinal));
        });
        prepared.Dispose();
        Assert.Equal(0, device.ActiveTables);
    }

    /// <summary>All builtin types and the solid rectangle pipeline share one table in painter order.</summary>
    /// <returns>The asynchronous test completion.</returns>
    [Fact]
    public async Task HeterogeneousPipelinesAndImageMasksShareActualRegistrationsAsync()
    {
        var device = new ProfilingDevice();
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        using var target = new DispatchTexture();
        using IGraphicsTextureView view = target.CreateView();
        using var imageTexture = new DispatchTexture();
        using IGraphicsTextureView image = imageTexture.CreateView();
        using var maskTexture = new DispatchTexture { Format = TextureFormat.R8Unorm };
        using IGraphicsTextureView mask = maskTexture.CreateView();
        using var sampler = new DispatchSampler();
        var canvas = new Canvas2D();
        var path = new PathBuilder2D();
        path.MoveTo(Vector2.Zero);
        path.LineTo(new(8, 0));
        path.LineTo(new(0, 8));
        path.Close();
        Path2D triangle = path.Build();
        var rect = new Rect2D(0, 0, 8, 8);
        var paint = PaintGraph2D.Solid(new(1, 0, 0, 0.5f));
        canvas.Begin();
        canvas.FillRect(rect, paint);
        canvas.ClipRect(new(0, 0, 14, 14));
        canvas.FillRoundedRect(rect, 1, paint);
        canvas.FillCircle(new(4), 3, paint);
        canvas.DrawLine(Vector2.Zero, new(8), paint, new());
        canvas.FillPath(triangle, paint);
        canvas.StrokePath(triangle, paint, new());
        canvas.DrawImage(new(image, sampler), rect, rect);
        canvas.DrawCoverageMask(new(mask, sampler), rect, rect, paint);
        canvas.DrawDistanceField(new(mask, sampler, DistanceFieldEncoding2D.Sdf, 4), rect, rect, paint);
        canvas.DrawMesh(new([new(Vector2.Zero, 1), new(new(8, 0), 1), new(new(0, 8), 1)], [0, 1, 2]), paint);
        canvas.DrawPaintGraph(paint);
        using PreparedDraw2D prepared = await renderer.PrepareAsync(canvas.End(), new() { View = view });
        ArgumentTableDesc desc = Assert.Single(device.Tables);
        Assert.Equal(3u, desc.TextureCapacity);
        Assert.Equal(2u, desc.SamplerCapacity);
        Assert.Equal(7u, desc.BufferCapacity);
        using var commands = new ProfilingCommands();
        prepared.Record(commands);
        Assert.Single(commands.Events.OfType<IArgumentTable>().Distinct());
        Assert.Equal(11u, Assert.Single(commands.Events.OfType<DrawRangeArguments2D>()).RecordCount);
        Assert.Equal((6u, 11u), Assert.Single(commands.Events.OfType<(uint, uint)>()));
        Assert.Equal(11, device.Records.Count);
        IArgumentTable table = Assert.Single(commands.Events.OfType<IArgumentTable>().Distinct());
        Assert.All(device.Instructions, instruction =>
        {
            Assert.Same(table, ProfilingDevice.Owner(instruction.Texture));
            Assert.Same(table, ProfilingDevice.Owner(instruction.Sampler));
        });
        Assert.All(commands.Events.OfType<DrawRangeArguments2D>(), arguments =>
        {
            Assert.Same(table, ProfilingDevice.Owner(arguments.Records));
            Assert.Same(table, ProfilingDevice.Owner(arguments.PageMetadata));
        });
    }

    /// <summary>New cache assets share while hits retain their original sealed identities.</summary>
    /// <returns>The asynchronous test completion.</returns>
    [Fact]
    public async Task RetainedAssetsShareNewPlanAndReuseWithoutRebindingAsync()
    {
        var device = new ProfilingDevice();
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillCircle(new(8), 3, new Color2D(1, 0, 0, 1));
        DrawList2D content = canvas.End();
        using var snapshot = new DrawingSnapshot2D(1, [new(default, content, Matrix3x2.Identity, [], 1, 1), new(default, content, Matrix3x2.CreateTranslation(1, 0), [], 1, 1)]);
        for (int frameIndex = 0; frameIndex < 2; frameIndex++)
        {
            using PreparedDrawingFrame2D frame = await renderer.PrepareAsync(snapshot, new() { View = view });
            using var commands = new ProfilingCommands();
            frame.Record(commands);
            commands.Submit();
            frame.AttachSubmission(new CompletedSubmission());
            commands.Dispose();
            frame.Retire();
            Assert.Equal(1, device.ActiveTables);
        }

        Assert.Single(device.Tables);
        Assert.Equal(14u, device.Tables[0].BufferCapacity);
        Assert.Equal(2, renderer.RetentionStatistics.ReusedRuns);
        renderer.Dispose();
        Assert.Equal(0, device.ActiveTables);
    }

    /// <summary>Unrecorded frames own independent plans until their own return, at depth one or three.</summary>
    /// <param name="depth">The frame budget and outstanding frame count.</param>
    /// <returns>The asynchronous test completion.</returns>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task OutstandingFramesHaveIndependentPlanLifetimesAsync(int depth)
    {
        var device = new ProfilingDevice();
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault(), retentionOptions: new() { MaxFramesInFlight = depth });
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillCircle(new(8), 3, new Color2D(1, 0, 0, 1));
        using var snapshot = new DrawingSnapshot2D(1, [new(default, canvas.End(), Matrix3x2.Identity, [], 1, 1)]);
        var frames = new List<PreparedDrawingFrame2D>();
        try
        {
            for (int i = 0; i < depth; i++)
            {
                frames.Add(await renderer.PrepareAsync(snapshot, new() { View = view }));
            }

            Assert.Equal(depth, device.ActiveTables);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await renderer.PrepareAsync(snapshot, new() { View = view }));
            for (int i = frames.Count - 1; i >= 0; i--)
            {
                frames[i].Dispose();
                Assert.Equal(i, device.ActiveTables);
            }
        }
        finally
        {
            foreach (PreparedDrawingFrame2D frame in frames)
            {
                frame.Dispose();
            }
        }
    }

    /// <summary>A failed upload plus failed table disposal remains renderer-owned for a later retry.</summary>
    /// <returns>The asynchronous test completion.</returns>
    [Fact]
    public async Task FailedPreparationCleanupRemainsOwnedAsync()
    {
        var device = new ProfilingDevice { FailCopy = 2, FailTableDisposeOnce = true };
        var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillCircle(new(8), 3, new Color2D(1, 0, 0, 1));
        await Assert.ThrowsAsync<AggregateException>(async () => await renderer.PrepareAsync(canvas.End(), new() { View = view }));
        Assert.Equal(1, device.ActiveTables);
        renderer.Dispose();
        renderer.Dispose();
        Assert.Equal(0, device.ActiveTables);
    }

    /// <summary>Evicting one asset cannot dispose the table still used by an independently retained sibling.</summary>
    /// <returns>The asynchronous test completion.</returns>
    [Fact]
    public async Task IndependentAssetEvictionKeepsSiblingTableAliveAsync()
    {
        var device = new ProfilingDevice();
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault(), retentionOptions: new() { MaxCachedAssets = 1 });
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillCircle(new(8), 3, new Color2D(1, 0, 0, 1));
        DrawList2D content = canvas.End();
        using var both = new DrawingSnapshot2D(1, [new(default, content, Matrix3x2.Identity, [], 1, 1), new(default, content, Matrix3x2.CreateTranslation(1, 0), [], 1, 1)]);
        using (PreparedDrawingFrame2D frame = await renderer.PrepareAsync(both, new() { View = view }))
        {
            using var commands = new ProfilingCommands();
            frame.Record(commands);
            commands.Submit();
            frame.AttachSubmission(new CompletedSubmission());
            commands.Dispose();
            frame.Retire();
        }

        Assert.Equal(1, renderer.RetentionStatistics.CachedAssets);
        Assert.Equal(1, device.ActiveTables);
        using var sibling = new DrawingSnapshot2D(2, [new(default, content, Matrix3x2.CreateTranslation(1, 0), [], 1, 1)]);
        using (PreparedDrawingFrame2D frame = await renderer.PrepareAsync(sibling, new() { View = view }))
        {
            using var commands = new ProfilingCommands();
            frame.Record(commands);
            commands.Dispose();
            frame.AbortUnsubmitted();
        }

        Assert.Single(device.Tables);
        Assert.Equal(1, renderer.RetentionStatistics.ReusedRuns);
        renderer.Dispose();
        Assert.Equal(0, device.ActiveTables);
    }

    /// <summary>A custom renderer joins only through the public context and deferred real-reference connection.</summary>
    /// <returns>The asynchronous test completion.</returns>
    [Fact]
    public async Task CustomRendererExplicitlyParticipatesThroughPublicContextAsync()
    {
        var device = new ProfilingDevice();
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        var custom = new ParticipatingCircleRenderer(view);
        var defaults = DrawingCommandRenderers2D.CreateDefault();
        var renderers = new DrawingCommandRenderers2D(defaults.FillRect, defaults.FillRoundedRect, custom, defaults.DrawLine, defaults.FillPath, defaults.StrokePath, defaults.DrawImage, defaults.DrawCoverageMask, defaults.DrawDistanceField, defaults.DrawMesh, defaults.DrawPaintGraph);
        using var renderer = new Renderer2D(device, renderers);
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new(0, 0, 4, 4), new Color2D(1, 0, 0, 1));
        canvas.FillCircle(new(8), 3, new Color2D(1, 0, 0, 1));
        using PreparedDraw2D prepared = await renderer.PrepareAsync(canvas.End(), new() { View = view });
        Assert.True(custom.Materialized);
        Assert.Single(device.Tables);
        using var commands = new ProfilingCommands();
        prepared.Record(commands);
        Assert.Single(commands.Events.OfType<IArgumentTable>().Distinct());
        prepared.Dispose();
        Assert.Equal(0, device.ActiveTables);
    }

    private sealed class ParticipatingCircleRenderer(IGraphicsTextureView view) : IFillCircleRenderer2D
    {
        internal bool Materialized { get; private set; }

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillCircleCommand2D> commands, CancellationToken cancellationToken)
        {
            DrawingArgumentPlan2D plan = context.ArgumentPlan;
            var batch = new ParticipatingBatch(plan, plan.Retain());
            IGpuRef<IGraphicsTextureView>? image = null;
            plan.RegisterTexture(view, reference => image = reference);
            plan.DeferUpload(_ =>
            {
                Assert.Same(plan.Table, ProfilingDevice.Owner(image!));
                Materialized = true;
                return ValueTask.CompletedTask;
            });
            return ValueTask.FromResult<IPreparedCommandBatch2D>(batch);
        }
    }

    private sealed class ParticipatingBatch(DrawingArgumentPlan2D plan, IDisposable lease) : IPreparedCommandBatch2D
    {
        public void DeclareResources(IDrawingResourceDeclarations2D resources)
        {
        }

        public void Record(IGraphicsCommandBuffer commands, IGraphicsTextureView target)
        {
            IRenderEncoder encoder = commands.BeginRenderPass(new() { ColorAttachments = [new() { View = target, LoadOp = AttachmentLoadOp.Load, StoreOp = AttachmentStoreOp.Store }] });
            encoder.SetArgumentTable(plan.Table);
            encoder.End();
        }

        public void Dispose() => lease.Dispose();
    }

    private sealed class CompletedSubmission : IGraphicsSubmission
    {
        public SubmissionStatus Status => SubmissionStatus.Completed;

        public ValueTask WaitAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }
}
