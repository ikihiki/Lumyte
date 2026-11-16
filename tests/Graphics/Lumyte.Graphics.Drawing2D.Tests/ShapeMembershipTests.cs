using Lumyte.Graphics.Abstractions;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Deterministic compute dispatch, ABI, lifetime and budget contracts, without executing a shader on the CPU.</summary>
public sealed class ShapeMembershipTests
{
    private static DeviceCaps ComputeCaps => new() { ShaderTarget = ShaderTarget.SpirV, MaxBufferSize = 1UL << 30, MaxComputeInvocationsPerWorkgroup = 256, MaxComputeWorkgroupSizeX = 16, MaxComputeWorkgroupSizeY = 16, MaxComputeWorkgroupSizeZ = 1, MaxComputeWorkgroupsPerDimension = 65535 };

    /// <summary>Workgroups cover exactly the conservative integer pixel rectangle, with guarded tails.</summary>
    /// <param name="width">The integer width.</param>
    /// <param name="height">The integer height.</param>
    /// <param name="x">The expected horizontal group count.</param>
    /// <param name="y">The expected vertical group count.</param>
    [Theory]
    [InlineData(1, 1, 1, 1)]
    [InlineData(16, 16, 1, 1)]
    [InlineData(17, 33, 2, 3)]
    public void DispatchUsesConservativeBounds(int width, int height, uint x, uint y)
    {
        ShapeMembership2D.Dispatch dispatch = ShapeMembership2D.Plan(7, new(10, 20, 10 + width, 20 + height), 13, ComputeCaps);
        Assert.Equal(x, dispatch.GroupsX);
        Assert.Equal(y, dispatch.GroupsY);
        Assert.Equal((uint)width, dispatch.Width);
        Assert.Equal((uint)height, dispatch.Height);
        Assert.Equal(13u, dispatch.Offset);
        Assert.Equal(7u, dispatch.Record);
    }

    /// <summary>Insufficient compute support is rejected instead of selecting the old evaluator.</summary>
    [Fact]
    public void CapabilityAndCapacityAreChecked()
    {
        Assert.Throws<NotSupportedException>(() => ShapeMembership2D.Plan(0, new(0, 0, 17, 17), 0, ComputeCaps with { MaxComputeInvocationsPerWorkgroup = 128 }));
        Assert.Throws<NotSupportedException>(() => ShapeMembership2D.Plan(0, new(0, 0, 17, 17), 0, ComputeCaps with { MaxComputeWorkgroupsPerDimension = 1 }));
        Assert.Throws<OverflowException>(() => ShapeMembership2D.Plan(0, new(0, 0, 16, 16), uint.MaxValue, ComputeCaps));
    }

    /// <summary>The opt-in ABI has a separate artifact and leaves historical artifact sizes intact.</summary>
    /// <param name="target">The compiled shader target.</param>
    [Theory]
    [InlineData(ShaderTarget.SpirV)]
    [InlineData(ShaderTarget.Wgsl)]
    [InlineData(ShaderTarget.Dxil)]
    public void MembershipRasterAbiIsSeparate(ShaderTarget target)
    {
        foreach (string stage in new[] { "vertex", "fragment" })
        {
            var artifact = ShaderArtifact.LoadEmbedded(typeof(Renderer2D).Assembly, "Lumyte.Drawing2D.draw-records-membership-" + stage + ".lshader");
            DrawRecordAbi2D.Validate(artifact, target, 8);
        }
    }

    /// <summary>Actual path preparation records compute before raster and never treats its output as resident.</summary>
    /// <param name="commandKind">Line, fill path, or stroke path.</param>
    /// <returns>The completed contract assertions.</returns>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task PathDispatchesBeforeRasterAndCannotReuseWritableOutputAsync(int commandKind)
    {
        var device = new ComputeDevice();
        using var state = new DrawingResourceState2D(device);
        using var plan = new DrawingArgumentPlan2D(device);
        var target = new DrawingTargetInfo2D { Description = new() { Width = 32, Height = 32, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment } };
        var context = new RenderPreparationContext2D(device, target, state, plan) { ComputeShapeMembership = true };
        var canvas = new Canvas2D();
        canvas.Begin();
        var paint = new SRgba<float>(1, 0, 0, 1);
        var style = new StrokeStyle2D { Width = 2 };
        var path = new PathBuilder2D();
        path.MoveTo(new(2, 2));
        path.LineTo(new(25, 2));
        path.LineTo(new(12, 25));
        path.Close();
        if (commandKind == 0)
        {
            canvas.DrawLine(new(2, 2), new(25, 25), paint, style);
        }
        else if (commandKind == 1)
        {
            canvas.FillPath(path.Build(), paint);
        }
        else
        {
            canvas.StrokePath(path.Build(), paint, style);
        }

        DrawingCommand2D[] source = canvas.End().Commands.ToArray();
        using var batch = (PreparedRecordBatch2D)await PreparedRecordBatch2D.PrepareAsync<DrawingCommand2D>(context, source, default);
        Assert.True(batch.HasShapeMembership);
        Assert.Throws<NotSupportedException>(() => batch.GraphPages.ToArray());
        ShapeMembershipPassPage2D passes = Assert.Single(batch.GraphPassPages);
        Assert.True(passes.HasCompute);
        await plan.SealAsync();
        using var texture = new DispatchTexture { Width = 32, Height = 32 };
        using IGraphicsTextureView view = texture.CreateView();
        using var commands = new ProfilingCommands();
        batch.Record(commands, view);
        int dispatch = commands.Events.FindIndex(value => value is ValueTuple<string, uint, uint, uint> call && call.Item1 == "dispatch");
        int raster = commands.Events.FindIndex(value => value is RecordedDrawRange);
        Assert.True(dispatch >= 0 && raster > dispatch);
        Assert.Single(commands.Events.OfType<ShapeMembershipArguments2D>());
        Assert.Contains(commands.Events, value => value is BufferBarrierDesc<uint> barrier && barrier.Before.Access.HasFlag(ResourceAccess.ShaderWrite) && barrier.After.Access.HasFlag(ResourceAccess.ShaderRead));
        Assert.Throws<InvalidOperationException>(() => batch.RecordRetained(commands, view, resident: true));
    }

    /// <summary>A rejected mask never allocates its GPU output or silently falls back.</summary>
    /// <returns>The completed budget assertions.</returns>
    [Fact]
    public async Task MaskWorkingBudgetRejectsBeforeGpuAllocationAsync()
    {
        var device = new ComputeDevice();
        using var state = new DrawingResourceState2D(device);
        using var plan = new DrawingArgumentPlan2D(device);
        var target = new DrawingTargetInfo2D { Description = new() { Width = 32, Height = 32, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment } };
        var context = new RenderPreparationContext2D(device, target, state, plan) { ComputeShapeMembership = true, MaxWorkingBytes = 1 };
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.DrawLine(new(2, 2), new(25, 25), new SRgba<float>(1, 0, 0, 1), new StrokeStyle2D { Width = 2 });
        DrawingCommand2D[] source = canvas.End().Commands.ToArray();
        await Assert.ThrowsAsync<NotSupportedException>(async () => await PreparedRecordBatch2D.PrepareAsync<DrawingCommand2D>(context, source, default));
        Assert.Equal(0, device.NativeBuffers);
    }

    /// <summary>Previously prepared owners consume admission before lowering or native allocation.</summary>
    /// <returns>The completed rejection assertions.</returns>
    [Fact]
    public async Task ExistingOwnersRejectNextBatchBeforeAllocationAsync()
    {
        var device = new ComputeDevice();
        using var state = new DrawingResourceState2D(device);
        using var plan = new DrawingArgumentPlan2D(device);
        var target = new DrawingTargetInfo2D { Description = new() { Width = 32, Height = 32, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment } };
        var context = new RenderPreparationContext2D(device, target, state, plan)
        {
            ComputeShapeMembership = true,
            MaxWorkingBytes = 65536,
            OtherWorkingBytes = 65000,
        };
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.DrawLine(new(2, 2), new(25, 25), new SRgba<float>(1, 0, 0, 1), new StrokeStyle2D { Width = 2 });
        DrawingCommand2D[] source = canvas.End().Commands.ToArray();
        await Assert.ThrowsAsync<NotSupportedException>(async () => await PreparedRecordBatch2D.PrepareAsync<DrawingCommand2D>(context, source, default));
        Assert.Equal(0, device.NativeBuffers);
    }

    /// <summary>Actual preparation of many ordinary paths uses derived capacities, not 32 MiB per path.</summary>
    /// <returns>The completed preparation assertions.</returns>
    [Fact]
    public async Task ManySmallPathsPrepareWithinDerivedBudgetAsync()
    {
        var device = new ComputeDevice();
        using var state = new DrawingResourceState2D(device);
        using var plan = new DrawingArgumentPlan2D(device);
        var target = new DrawingTargetInfo2D { Description = new() { Width = 32, Height = 32, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment } };
        var context = new RenderPreparationContext2D(device, target, state, plan) { ComputeShapeMembership = true, MaxWorkingBytes = 4 * 1024 * 1024 };
        var canvas = new Canvas2D();
        var path = new PathBuilder2D();
        path.MoveTo(new(2, 2));
        path.LineTo(new(6, 2));
        path.LineTo(new(2, 6));
        path.Close();
        Path2D triangle = path.Build();
        canvas.Begin();
        for (int i = 0; i < 64; i++)
        {
            canvas.FillPath(triangle, new SRgba<float>(1, 0, 0, 1));
        }

        DrawingCommand2D[] source = canvas.End().Commands.ToArray();
        using var batch = (PreparedRecordBatch2D)await PreparedRecordBatch2D.PrepareAsync<DrawingCommand2D>(context, source, default);
        Assert.True(batch.HasShapeMembership);
        Assert.InRange(batch.EstimatedBytes, 1, context.MaxWorkingBytes);
        Assert.True(device.NativeBuffers > 0);
    }

    /// <summary>Fractional clips and nested paint clips fit a default WebGPU storage budget.</summary>
    /// <returns>The completed preparation and binding checks.</returns>
    [Fact]
    public async Task FractionalClippedPathFitsSevenBufferSlotsAsync()
    {
        var device = new ComputeDevice(ComputeCaps with { ArgumentTableBindingLimits = new() { BuffersPerDraw = 7 } });
        using var state = new DrawingResourceState2D(device);
        using var plan = new DrawingArgumentPlan2D(device);
        var target = new DrawingTargetInfo2D { Description = new() { Width = 8, Height = 8, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment } };
        var context = new RenderPreparationContext2D(device, target, state, plan) { ComputeShapeMembership = true };
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.ClipRect(new(0.5f, 0, 7, 8));
        var path = new PathBuilder2D();
        path.MoveTo(new(0, 2));
        path.LineTo(new(8, 2));
        canvas.StrokePath(path.Build(), new SRgba<float>(1, 0, 0, 1), new() { Width = 2 });
        DrawingCommand2D[] source = canvas.End().Commands.ToArray();
        using var batch = (PreparedRecordBatch2D)await PreparedRecordBatch2D.PrepareAsync<DrawingCommand2D>(context, source, default);
        Assert.True(batch.HasShapeMembership);
        await plan.SealAsync();
        using var texture = new DispatchTexture { Width = 8, Height = 8 };
        using IGraphicsTextureView view = texture.CreateView();
        using var commands = new ProfilingCommands();
        batch.Record(commands, view);
        Assert.Single(commands.Events.OfType<ShapeMembershipArguments2D>());
    }

    private sealed class ComputeDevice(DeviceCaps? caps = null) : IGraphicDevice
    {
        private readonly ProfilingDevice _inner = new() { Caps = caps ?? ComputeCaps };

        public event EventHandler<GraphicsDeviceLostEventArgs>? DeviceLost
        {
            add
            {
            }

            remove
            {
            }
        }

        public GraphicsDeviceStatus Status => _inner.Status;

        public GraphicsDeviceLoss? Loss => null;

        public DeviceCaps Caps => _inner.Caps;

        public IGraphicsQueue Queue => _inner.Queue;

        internal int NativeBuffers => _inner.NativeBuffers;

        public ValueTask WaitForRetirementAsync(CancellationToken cancellationToken = default) => _inner.WaitForRetirementAsync(cancellationToken);

        public BufferLayout<T> GetBufferLayout<T>()
            where T : unmanaged => _inner.GetBufferLayout<T>();

        public IGraphicsBuffer<T> CreateBuffer<T>(BufferDesc<T> desc)
            where T : unmanaged => _inner.CreateBuffer(desc);

        public IGraphicsTexture CreateTexture(TextureDesc desc) => _inner.CreateTexture(desc);

        public IGraphicsSampler CreateSampler(SamplerDesc desc) => _inner.CreateSampler(desc);

        public IArgumentTable CreateArgumentTable(ArgumentTableDesc desc) => _inner.CreateArgumentTable(desc);

        public IGraphicsShader CreateShader(ShaderArtifact artifact) => _inner.CreateShader(artifact);

        public IGraphicsSemaphore CreateSemaphore() => _inner.CreateSemaphore();

        public IGraphicsCommandBuffer CreateCommandBuffer(CommandBufferDesc desc) => _inner.CreateCommandBuffer(desc);

        public TextureCopyLayout GetTextureCopyLayout(TextureFormat format) => _inner.GetTextureCopyLayout(format);

        public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc) => _inner.CreateGraphicsPipeline(desc);

        public IGraphicsComputePipeline CreateComputePipeline(ComputePipelineDesc desc) => new Pipeline(desc);

        public IGraphicsShaderDataBuffer<T> CreateShaderDataBuffer<T>(ShaderArtifact artifact, ulong count, MemoryPreference memory = MemoryPreference.Automatic)
            where T : struct, IShaderArguments
        {
            IGraphicsShaderDataBuffer<T> buffer = _inner.CreateShaderDataBuffer<T>(artifact, count, memory);
            return typeof(T) == typeof(ShapeMembershipMetadata2D) ? new MembershipBuffer<T>(buffer) : buffer;
        }

        private sealed class MembershipBuffer<T>(IGraphicsShaderDataBuffer<T> inner) : IGraphicsShaderDataBuffer<T>
            where T : struct, IShaderArguments
        {
            public ulong Count => inner.Count;

            public ulong SizeInBytes => Count * 96;

            public ulong ShaderElementStrideInBytes => 96;

            public MemoryPreference Memory => inner.Memory;

            public bool IsMapped => inner.IsMapped;

            public void CopyFrom(ReadOnlySpan<T> source, ulong elementOffset = 0) => inner.CopyFrom(source, elementOffset);

            public ValueTask MapAsync(CancellationToken cancellationToken = default) => inner.MapAsync(cancellationToken);

            public void Unmap() => inner.Unmap();

            public ShaderDataSlice<T> SliceElements(ulong offset, ulong count) => new(this, offset, count);

            public void Dispose() => inner.Dispose();
        }

        private sealed class Pipeline(ComputePipelineDesc desc) : IGraphicsComputePipeline
        {
            public ComputePipelineDesc Desc => desc;

            public void Dispose()
            {
            }
        }
    }
}
