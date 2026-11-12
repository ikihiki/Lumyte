using System.Buffers.Binary;
using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Graphics.RenderGraph;
using Xunit;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks the fixed logical ABI independently of native GPU allocation.</summary>
public sealed class DrawRecordTests
{
    /// <summary>Both shader stages and every offline backend retain the exact canonical offsets.</summary>
    /// <param name="target">The compiled backend target.</param>
    [Theory]
    [InlineData(ShaderTarget.SpirV)]
    [InlineData(ShaderTarget.Wgsl)]
    [InlineData(ShaderTarget.Dxil)]
    public void EveryBackendMatchesCanonicalWire(ShaderTarget target)
    {
        for (int mode = 0; mode <= 4; mode++)
        {
            string variant = mode switch { 0 => "-basic", 1 => "-mask", 2 => "-image", 4 => "-solid", _ => string.Empty };
            foreach (string stage in new[] { "vertex", "fragment" })
            {
                var artifact = ShaderArtifact.LoadEmbedded(typeof(Renderer2D).Assembly, "Lumyte.Drawing2D.draw-records" + variant + "-" + stage + ".lshader");
                DrawRecordAbi2D.Validate(artifact, target, mode);
            }
        }
    }

    /// <summary>Half-open empty/full selections are valid without overflow-prone addition.</summary>
    /// <param name="first">The page-relative start.</param>
    /// <param name="count">The selected count.</param>
    /// <param name="available">The logical page count.</param>
    [Theory]
    [InlineData(0u, 0u, 0u)]
    [InlineData(0u, 1u, 1u)]
    [InlineData(0u, 340u, 340u)]
    [InlineData(0u, 341u, 341u)]
    [InlineData(16u, 5u, 341u)]
    [InlineData(340u, 1u, 341u)]
    [InlineData(341u, 0u, 341u)]
    [InlineData(uint.MaxValue, 0u, uint.MaxValue)]
    public void RangePreservesHalfOpenBoundaries(uint first, uint count, uint available)
        => DrawRecordPage2D.ValidateRange(first, count, available);

    /// <summary>Underflow, overflow and one-past selections are rejected before encoding.</summary>
    /// <param name="first">The page-relative start.</param>
    /// <param name="count">The selected count.</param>
    [Theory]
    [InlineData(342u, 0u)]
    [InlineData(341u, 1u)]
    [InlineData(340u, 2u)]
    [InlineData(uint.MaxValue, 1u)]
    [InlineData(1u, uint.MaxValue)]
    public void InvalidRangeCannotWrap(uint first, uint count)
        => Assert.Throws<ArgumentOutOfRangeException>(() => DrawRecordPage2D.ValidateRange(first, count, 341));

    /// <summary>Binding alignment constrains stream starts, never each individual record.</summary>
    /// <param name="offset">The preceding allocation end.</param>
    /// <param name="stride">The typed wire stride.</param>
    /// <param name="expected">The aligned stream start.</param>
    [Theory]
    [InlineData(0ul, 96ul, 0ul)]
    [InlineData(32768ul, 96ul, 33024ul)]
    [InlineData(1ul, 80ul, 1280ul)]
    [InlineData(1ul, 128ul, 256ul)]
    public void StreamStartsUseLeastCommonMultiple(ulong offset, ulong stride, ulong expected)
    {
        Assert.Equal(expected, DrawRecordPage2D.AlignedStart(offset, 256, stride));
        Assert.Throws<OverflowException>(() => DrawRecordPage2D.AlignedStart(ulong.MaxValue, 256, stride));
    }

    /// <summary>The generated codec preserves integer bits, including NaN-looking payload values.</summary>
    [Fact]
    public void GeneratedCodecDoesNotNumericallyConvertIntegerLanes()
    {
        var record = new DrawRecord2D(2, 0, 0x7fc01234, 0xffffffff, new(1, 2, 3, 4), new(1, 0, 0, 0), new(0, 1, 0, 0), new(1, 2, 3, 4), 0x80000000, 3, 0, 0x7fa12345);
        ShaderValueSnapshot snapshot = IShaderArguments.Capture(record);
        Assert.Equal(12, snapshot.Values.Count);
        Assert.Equal(0x7fc01234u, BinaryPrimitives.ReadUInt32LittleEndian(snapshot.Values.Single(value => value.Path == "PaintId").Data.Span));
        Assert.Equal(0x7fa12345u, BinaryPrimitives.ReadUInt32LittleEndian(snapshot.Values.Single(value => value.Path == "ExtraBits").Data.Span));
    }

    /// <summary>All eleven public commands lower to exactly one logical record with separate typed payloads.</summary>
    [Fact]
    public void EveryBuiltinLowersToOneRecord()
    {
        var path = new PathBuilder2D();
        path.MoveTo(new(1, 1));
        path.LineTo(new(8, 1));
        path.LineTo(new(4, 8));
        path.Close();
        Path2D triangle = path.Build();
        var color = new Color2D(1, 0, 0, 1);
        var paint = PaintGraph2D.Solid(color);
        var sampler = new DispatchSampler();
        var imageView = new DispatchTextureView(new DispatchTexture { Width = 4, Height = 4, Usage = TextureUsage.Sampled, Format = TextureFormat.Rgba8Unorm }, null);
        var maskView = new DispatchTextureView(new DispatchTexture { Width = 4, Height = 4, Usage = TextureUsage.Sampled, Format = TextureFormat.R8Unorm }, null);
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new(1, 1, 8, 8), paint);
        canvas.FillRoundedRect(new(1, 1, 8, 8), 2, paint);
        canvas.FillCircle(new(4, 4), 3, paint);
        canvas.DrawLine(new(1, 1), new(8, 8), paint, new() { Width = 1 });
        canvas.FillPath(triangle, paint);
        canvas.StrokePath(triangle, paint, new() { Width = 1 });
        canvas.DrawImage(new(imageView, sampler), new(0, 0, 4, 4), new(1, 1, 8, 8));
        canvas.DrawCoverageMask(new(maskView, sampler), new(0, 0, 4, 4), new(1, 1, 8, 8), paint);
        canvas.DrawDistanceField(new(maskView, sampler, DistanceFieldEncoding2D.Sdf, 4), new(0, 0, 4, 4), new(1, 1, 8, 8), paint);
        canvas.DrawMesh(new([new(new(1, 1), 1), new(new(8, 1), 1), new(new(4, 8), 0.5f)], [0, 1, 2]), paint);
        canvas.DrawPaintGraph(paint);
        var page = new DrawRecordPage2D();
        using var plan = new ArgumentTablePlan(new ProfilingDevice());
        var target = new DrawingTargetInfo2D { Description = new() { Width = 16, Height = 16, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment } };
        var clips = new ClipNormalizationCache2D(target);
        foreach (DrawingCommand2D command in canvas.End().Commands)
        {
            var program = new DrawingProgram2D(plan.CreateShaderResources(), target, clips);
            uint root = program.Compile(command);
            program.ValidateFiniteData();
            page.Add(command, program, root, new(0, 0, 16, 16), clips.Get(command.State.Clip, Matrix3x2.Identity, command.State.Clip.Elements));
        }

        Assert.Equal(new uint[] { 1, 2, 3, 5, 4, 5, 1, 7, 8, 6, 9 }, page.Records.Select(record => record.Kind));
        Assert.Equal(2, page.Masks.Count);
        Assert.Single(page.Images);
        Assert.Equal(3, page.Paints.Count); // Transparent, shared solid and image paint.
        Assert.All(page.Records, record => Assert.Equal(0u, record.Flags));
        Assert.Equal(BitConverter.SingleToUInt32Bits(2), page.Records[1].ExtraBits);
        Assert.Equal(0u, page.Records[7].PayloadOffset16);
        Assert.Equal(5u, page.Records[8].PayloadOffset16);
        Assert.Equal(1u, page.Records[9].Mode);
        page.ReleasePackingData();
        Assert.Equal(0, page.Numeric.Capacity);
        Assert.Equal(0, page.Records.Capacity);
        Assert.Equal(0, page.Images.Capacity);
        Assert.Equal(0, page.Masks.Capacity);
    }

    /// <summary>Prepared range selection changes only GPU arguments, never payload packing or IDs.</summary>
    /// <returns>The completed CPU preparation.</returns>
    [Fact]
    public async Task SelectedRangeDoesNotResliceOrRecompileAsync()
    {
        var device = new ProfilingDevice();
        using var state = new DrawingResourceState2D(device);
        using var plan = new DrawingArgumentPlan2D(device);
        var target = new DrawingTargetInfo2D { Description = new() { Width = 32, Height = 32, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment } };
        var context = new RenderPreparationContext2D(device, target, state, plan);
        var canvas = new Canvas2D();
        canvas.Begin();
        for (int i = 0; i < 25; i++)
        {
            canvas.FillCircle(new(8), 4, new Color2D(1, 0, 0, 0.5f));
        }

        DrawingCommand2D[] source = canvas.End().Commands.ToArray();
        using var prepared = (PreparedRecordBatch2D)await PreparedRecordBatch2D.PrepareAsync<DrawingCommand2D>(context, source, default);
        await plan.SealAsync();
        int copies = device.Copies;
        using var texture = new DispatchTexture { Width = 32, Height = 32 };
        using IGraphicsTextureView view = texture.CreateView();
        using var commands = new ProfilingCommands();
        prepared.RecordRange(commands, view, 0, 16, 5);
        RecordedDrawRange selected = Assert.Single(commands.Events.OfType<RecordedDrawRange>());
        Assert.Equal(16u, selected.FirstRecord);
        Assert.Equal(5u, selected.RecordCount);
        Assert.Equal((6u, 5u), Assert.Single(commands.Events.OfType<(uint, uint)>()));
        Assert.Equal(copies, device.Copies);
        int events = commands.Events.Count;
        Assert.Throws<ArgumentOutOfRangeException>(() => prepared.RecordRange(commands, view, 0, uint.MaxValue, 5));
        Assert.Equal(events, commands.Events.Count);
        prepared.RecordRange(commands, view, 0, 25, 0);
        Assert.Equal(events, commands.Events.Count);
        prepared.RecordRange(commands, view, 0, 24, 1, resident: true);
        RecordedDrawRange last = commands.Events.OfType<RecordedDrawRange>().Last();
        Assert.Same(selected.Records, last.Records);
        Assert.Same(selected.PageMetadata, last.PageMetadata);
        Assert.Equal(copies, device.Copies);
    }

    /// <summary>Compatible mixed records form one contiguous range, with no 341-record allocation boundary.</summary>
    /// <param name="count">The logical draw count.</param>
    /// <returns>The completed CPU preparation and command encoding.</returns>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(340)]
    [InlineData(341)]
    [InlineData(342)]
    [InlineData(1000)]
    public async Task ActualAllocationsFollowNeededStreamsAsync(int count)
    {
        var device = new ProfilingDevice();
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        var canvas = new Canvas2D();
        canvas.Begin();
        for (int i = 0; i < count; i++)
        {
            if (i % 2 == 0)
            {
                canvas.FillRect(new(1, 1, 8, 8), new Color2D(1, 0, 0, 1));
            }
            else
            {
                canvas.FillCircle(new(4), 3, new Color2D(0, 1, 0, 1));
            }
        }

        using PreparedDraw2D prepared = await renderer.PrepareAsync(canvas.End(), new() { View = view });
        Assert.Equal(count, device.Records.Count);
        Assert.Equal(count == 0 ? 0 : 4, device.NativeBuffers);
        using var commands = new ProfilingCommands();
        prepared.Record(commands);
        Assert.Equal(count == 0 ? 0 : 1, commands.Events.OfType<RecordedDrawRange>().Count());
        Assert.Equal(count, commands.Events.OfType<RecordedDrawRange>().Sum(range => (long)range.RecordCount));
    }

    /// <summary>A palette range needs only two read streams plus the reserved WGSL remap; one fewer fails before publication.</summary>
    /// <param name="buffers">The effective nonreserved storage binding budget.</param>
    /// <returns>The completed preparation or preflight rejection.</returns>
    [Theory]
    [InlineData(2u)]
    [InlineData(1u)]
    public async Task PhysicalBindingBudgetIncludesEveryStreamAsync(uint buffers)
    {
        var device = new ProfilingDevice { Caps = new() { ShaderTarget = ShaderTarget.Wgsl, MaxBufferSize = 1UL << 30, MaxStorageBuffersPerStage = buffers + 1, ArgumentTableBindingLimits = new() { BufferSlots = buffers, BuffersPerDraw = buffers, ReservedBindings = 1, CombinedBindings = buffers + 3 } } };
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new(0, 0, 8, 8), new Color2D(1, 0, 0, 1));
        DrawList2D list = canvas.End();
        if (buffers == 2)
        {
            using PreparedDraw2D prepared = await renderer.PrepareAsync(list, new() { View = view });
            Assert.Equal(2u, Assert.Single(device.Tables).BufferCapacity);
        }
        else
        {
            await Assert.ThrowsAsync<NotSupportedException>(async () => await renderer.PrepareAsync(list, new() { View = view }));
            Assert.Empty(device.Tables);
            Assert.Equal(0, device.Writes);
        }
    }

    /// <summary>A CPU page edit invalidates the containing contiguous GPU range without claiming dirty-page-only GPU work.</summary>
    /// <returns>The completed mocked submissions and retirement checks.</returns>
    [Fact]
    public async Task PartialUpdateRebuildsTheImmutableContainingRangeAsync()
    {
        var device = new ProfilingDevice();
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        using var scene = new DrawingScene2D();
        var canvas = new Canvas2D();
        canvas.Begin();
        for (int i = 0; i < 1200; i++)
        {
            canvas.FillRect(new(0, 0, 8, 8), new Color2D(1, 0, 0, 1));
        }

        DrawList2D original = canvas.End();
        DrawingNodeId2D node = scene.Insert(scene.Root, 0, original);
        RecordedDrawRange[] before = await RenderFrameAsync();
        Assert.Equal(new uint[] { 1200 }, before.Select(range => range.RecordCount));
        canvas.Begin();
        for (int i = 0; i < 64; i++)
        {
            canvas.FillRect(new(0, 0, 8, 8), new Color2D(0, 0, 1, 1));
        }

        scene.ReplaceContent(node, original.WithPage(0, canvas.End()));
        RecordedDrawRange[] after = await RenderFrameAsync();
        Assert.NotSame(before[0].Records, after[0].Records);
        Assert.Single(after);
        Assert.Equal(0, renderer.RetentionStatistics.ReusedRuns);
        Assert.Equal(2, renderer.RetentionStatistics.CompiledRuns);

        async Task<RecordedDrawRange[]> RenderFrameAsync()
        {
            using DrawingSceneReadLease2D read = scene.Borrow();
            using PreparedDrawingFrame2D frame = await renderer.PrepareAsync(read, new() { View = view });
            using var commands = new ProfilingCommands();
            frame.Record(commands);
            RecordedDrawRange[] ranges = commands.Events.OfType<RecordedDrawRange>().ToArray();
            commands.Submit();
            frame.AttachSubmission(new Completed());
            commands.Dispose();
            frame.Retire();
            return ranges;
        }
    }

    /// <summary>Zero source or destination mask/field extents stay transparent before page admission.</summary>
    /// <param name="field">Uses a signed-distance field instead of a coverage mask.</param>
    /// <param name="destination">Empties destination rather than source.</param>
    /// <returns>The completed empty preparation.</returns>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task EmptySampledShapesDoNotAllocateRecordsAsync(bool field, bool destination)
    {
        var device = new ProfilingDevice();
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        using var texture = new DispatchTexture();
        using IGraphicsTextureView target = texture.CreateView();
        using var mask = new DispatchTexture { Format = TextureFormat.R8Unorm, Usage = TextureUsage.Sampled };
        using IGraphicsTextureView sourceView = mask.CreateView();
        using var sampler = new DispatchSampler();
        var paint = PaintGraph2D.Solid(new(1, 0, 0, 1));
        Rect2D source = destination ? new(0, 0, 8, 8) : new(0, 0, 0, 8);
        Rect2D bounds = destination ? new(1, 1, 0, 8) : new(1, 1, 8, 8);
        var canvas = new Canvas2D();
        canvas.Begin();
        if (field)
        {
            canvas.DrawDistanceField(new(sourceView, sampler, DistanceFieldEncoding2D.Sdf, 4), source, bounds, paint);
        }
        else
        {
            canvas.DrawCoverageMask(new(sourceView, sampler), source, bounds, paint);
        }

        using PreparedDraw2D prepared = await renderer.PrepareAsync(canvas.End(), new() { View = target });
        Assert.Empty(device.Records);
        Assert.Empty(device.Tables);
        Assert.Equal(0, device.NativeBuffers);
    }

    /// <summary>Built-in graph passes close at actual numeric-page limits, not only command capacity.</summary>
    /// <returns>The prepared, partitioned graph and materialization checks.</returns>
    [Fact]
    public async Task GraphPassesFollowActualNumericPagesAsync()
    {
        var device = new ProfilingDevice { Caps = new() { ShaderTarget = ShaderTarget.Wgsl, MaxBufferSize = 1UL << 30, MaxStorageBufferBindingSize = 65536, ArgumentTableBindingLimits = new() { BufferSlots = 7, BuffersPerDraw = 7, MaximumBufferRangeBytes = 65536 } } };
        using var graph = new Graph(device);
        using var texture = new DispatchTexture();
        RenderGraphTexture target = graph.ImportTexture(texture, TextureState.ColorAttachment, new(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite));
        var path = new PathBuilder2D();
        path.MoveTo(new(0, 0));
        for (int i = 1; i < 2200; i++)
        {
            path.LineTo(new(i % 16, (i / 16) % 16));
        }

        path.Close();
        Path2D geometry = path.Build();
        var canvas = new RenderGraphCanvas2D();
        canvas.Begin();
        canvas.FillPath(geometry, new Color2D(1, 0, 0, 1));
        var distinct = new PathBuilder2D();
        distinct.MoveTo(new(0.125f, 0));
        for (int i = 1; i < 2200; i++)
        {
            distinct.LineTo(new((i % 16) + 0.125f, (i / 16) % 16));
        }

        distinct.Close();
        canvas.FillPath(distinct.Build(), new Color2D(0, 0, 1, 1));
        using var renderer = new RenderGraphRenderer2D(DrawingCommandRenderers2D.CreateDefault(), canvas.End(), new() { Texture = target, Description = new() { Width = 16, Height = 16, Format = TextureFormat.Rgba8Srgb, Usage = texture.Usage } }, new DrawingGraphBindings2D(graph));
        using PreparedDrawingPasses2D prepared = await graph.Append2D(renderer);
        using CompiledRenderGraph plan = graph.Compile();
        Assert.Equal(2, plan.PassNames.Count(name => name == "drawing2d.batch"));
        using var commands = new ProfilingCommands();
        plan.Record(commands);
        Assert.Equal(2, commands.Events.OfType<RecordedDrawRange>().Count());
        Assert.All(device.Tables, table => Assert.Equal(4u, table.BufferCapacity));
    }

    /// <summary>Distinct incoming masks count toward page limits; one oversized draw fails before publication.</summary>
    /// <param name="oversized">Uses one mask that cannot fit its physical binding.</param>
    /// <returns>The completed partitioning or early rejection checks.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncomingOuterClipBytesArePreflightedAsync(bool oversized)
    {
        var device = new ProfilingDevice { Caps = new() { ShaderTarget = ShaderTarget.Wgsl, MaxBufferSize = 1UL << 30, MaxStorageBufferBindingSize = 65536, ArgumentTableBindingLimits = new() { MaximumBufferRangeBytes = 65536 } } };
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        using var texture = new DispatchTexture { Width = 128, Height = 128 };
        using IGraphicsTextureView view = texture.CreateView();
        var canvas = new Canvas2D();
        canvas.Begin();
        for (int i = 0; i < (oversized ? 1 : 5); i++)
        {
            float extent = oversized ? 128 : 64;
            var path = new PathBuilder2D();
            path.MoveTo(new(0.125f + i, 0.125f));
            path.LineTo(new(extent, 0.25f + i));
            path.LineTo(new(0.25f, extent));
            path.Close();
            canvas.Save();
            canvas.ClipPath(path.Build());
            canvas.FillRect(new(0, 0, extent, extent), new Color2D(1, 0, 0, 1));
            canvas.Restore();
        }

        DrawList2D list = canvas.End();
        if (oversized)
        {
            await Assert.ThrowsAsync<NotSupportedException>(async () => await renderer.PrepareAsync(list, new() { View = view }));
            Assert.Equal(0, device.NativeBuffers);
            Assert.Equal(0, device.Writes);
            Assert.Empty(device.Tables);
        }
        else
        {
            using PreparedDraw2D prepared = await renderer.PrepareAsync(list, new() { View = view });
            using var commands = new ProfilingCommands();
            prepared.Record(commands);
            Assert.Equal(2, commands.Events.OfType<RecordedDrawRange>().Select(range => range.Records).Distinct().Count());
            Assert.Equal(5, device.Records.Count);
            foreach (ShaderValue value in device.ShaderValues.SelectMany(snapshot => snapshot.Values).Where(value => value.IsReference && value.ValueType == typeof(Vector4)))
            {
                Assert.InRange(((IShaderReference)value.Reference!).SizeInBytes, 1ul, 65536ul);
            }
        }
    }

    /// <summary>Individually valid transforms cannot overwrite a transparent fallback after their product loses a finite inverse.</summary>
    /// <param name="field">Uses a distance-field paint leaf.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidCombinedImageInverseRemainsTransparent(bool field)
    {
        var scale = Matrix3x2.CreateScale(1e-10f);
        Assert.True(Matrix3x2.Invert(scale, out Matrix3x2 inverse));
        Assert.True(float.IsFinite(inverse.M11));
        Matrix3x2.Invert(scale * scale, out Matrix3x2 combined);
        Assert.False(float.IsFinite(combined.M11));
        using var image = new DispatchTexture { Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.Sampled };
        using IGraphicsTextureView view = image.CreateView();
        var pattern = new ImagePattern2D { View = view, Source = new(0, 0, 1, 1), PatternToLocal = scale };
        PaintGraph2D paint = field ? PaintGraph2D.DistanceField(pattern, 4, PaintGraph2D.Solid(new(1, 0, 0, 1))) : PaintGraph2D.ImagePattern(pattern);
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.Scale(new Vector2(1e-10f));
        canvas.DrawPaintGraph(paint);
        DrawingCommand2D command = Assert.Single(canvas.End().Commands);
        using var plan = new ArgumentTablePlan(new ProfilingDevice());
        var target = new DrawingTargetInfo2D { Description = new() { Width = 2, Height = 1, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment } };
        var program = new DrawingProgram2D(plan.CreateShaderResources(), target);
        uint root = program.Compile(command);
        DrawingInstruction leaf = program.Instructions[(int)program.Instructions[(int)root].Count];
        Assert.Equal(0u, leaf.Kind);
        Assert.Equal(Vector4.Zero, leaf.A);
        Assert.Empty(program.Views);
    }

    private sealed class Completed : IGraphicsSubmission
    {
        public SubmissionStatus Status => SubmissionStatus.Completed;

        public ValueTask WaitAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }
}
