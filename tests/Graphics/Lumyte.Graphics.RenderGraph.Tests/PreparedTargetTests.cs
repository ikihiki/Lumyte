using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.RenderGraph.Tests;

/// <summary>Managed ownership tests with fake native origins, not GPU qualification.</summary>
public sealed class PreparedTargetTests
{
    private static TextureDesc Description => new() { Width = 16, Height = 16, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource };

    /// <summary>Ordinary compile remains lazy.</summary>
    [Fact]
    public void NormalCompileDoesNotAllocate()
    {
        var device = new PreparedTargetDevice();
        using var graph = new RenderGraph(device);
        RenderGraphTexture resource = graph.CreateTexture(Description);
        graph.AddPass("write", _ => { }).UseTexture(resource, TextureState.ColorAttachment, new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite));
        graph.MarkOutput(resource);
        using CompiledRenderGraph plan = graph.Compile();
        Assert.Equal(0, device.Textures);
    }

    /// <summary>Preparation preserves transient identity and reuses exact texture and view at record.</summary>
    [Fact]
    public void PreparedTransientIsReusedByCompiledPlan()
    {
        var device = new PreparedTargetDevice();
        using var graph = new RenderGraph(device);
        RenderGraphTexture resource = graph.CreateTexture(Description);
        RenderGraph.PreparedTarget target = graph.PrepareTarget(resource);
        Assert.Same(target, graph.PrepareTarget(resource));
        IGraphicsTextureView view = target.View;
        Assert.True(resource.IsTransient);
        graph.AddPass("write", context => Assert.Same(view.Texture, context.GetTexture(resource)))
            .UseTexture(resource, TextureState.ColorAttachment, new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite));
        graph.MarkOutput(resource);
        using CompiledRenderGraph plan = graph.Compile();
        graph.Dispose();
        Assert.Same(view.Texture, plan.Resolve(resource));
        Assert.Same(view, plan.CreateView(resource, new() { Dimension = TextureViewDimension.D2 }));
        Assert.Equal(1, plan.Statistics.TransientResources);
        using var commands = new RecordingCommands();
        plan.Record(commands);
        commands.Dispose();
        plan.Dispose();
        Assert.Equal(1, device.Textures);
        Assert.Equal(new[] { "view", "texture" }, device.Lifetime);
    }

    /// <summary>Independent uses survive graph disposal and retry failed final native cleanup.</summary>
    /// <param name="textureFailure">Selects texture rather than view cleanup failure.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UsesOutliveGraphAndRetryCleanup(bool textureFailure)
    {
        var device = new PreparedTargetDevice();
        var graph = new RenderGraph(device);
        RenderGraph.PreparedTarget target = graph.PrepareTarget(graph.CreateTexture(Description));
        IDisposable first = target.AcquireUse();
        IDisposable last = target.AcquireUse();
        var texture = (PreparedTargetTexture)target.View.Texture;
        graph.Dispose();
        Assert.Same(texture.View, target.View);
        using (IDisposable nested = target.AcquireUse())
        {
            Assert.Empty(device.Lifetime);
        }

        first.Dispose();
        Assert.Empty(device.Lifetime);
        if (textureFailure)
        {
            texture.FailDispose = true;
        }
        else
        {
            texture.View!.FailDispose = true;
        }

        Assert.Throws<InvalidOperationException>(() => last.Dispose());
        last.Dispose();
        Assert.Equal(new[] { "view", "texture" }, device.Lifetime);
    }

    /// <summary>View creation failures retain the allocation on the original graph and allow retry.</summary>
    [Fact]
    public void ViewFailureRetainsOriginalAllocation()
    {
        var device = new PreparedTargetDevice { FailView = true };
        using var graph = new RenderGraph(device);
        RenderGraphTexture resource = graph.CreateTexture(Description);
        Assert.Throws<InvalidOperationException>(() => graph.PrepareTarget(resource));
        Assert.True(graph.IsBuilding);
        Assert.Empty(device.Lifetime);
        device.FailView = false;
        Assert.Same(device.LastTexture, graph.PrepareTarget(resource).View.Texture);
        Assert.Equal(1, device.Textures);
    }

    /// <summary>Failed creation remains graph owned and graph disposal can retry native cleanup.</summary>
    [Fact]
    public void UnpublishedOwnerCleanupRetriesThroughGraph()
    {
        var device = new PreparedTargetDevice { FailView = true };
        var graph = new RenderGraph(device);
        Assert.Throws<InvalidOperationException>(() => graph.PrepareTarget(graph.CreateTexture(Description)));
        device.LastTexture!.FailDispose = true;
        Assert.Throws<InvalidOperationException>(() => graph.Dispose());
        graph.Dispose();
        Assert.Equal(new[] { "texture" }, device.Lifetime);
    }

    /// <summary>Imports borrow textures, owning only newly created views.</summary>
    [Fact]
    public void ImportedTextureIsBorrowed()
    {
        var device = new PreparedTargetDevice();
        var texture = new PreparedTargetTexture(device);
        using var graph = new RenderGraph(device);
        RenderGraphTexture resource = graph.ImportTexture(texture, TextureState.Undefined, default);
        graph.PrepareTarget(resource);
        graph.Dispose();
        Assert.Equal(new[] { "view" }, device.Lifetime);
        Assert.False(texture.Disposed);
    }

    /// <summary>Foreign graphs, devices, invalid ranges and disposed origins are rejected.</summary>
    [Fact]
    public void InvalidSourcesDoNotConsumeCallerOwnership()
    {
        var device = new PreparedTargetDevice();
        using var graph = new RenderGraph(device);
        using var foreign = new RenderGraph(device);
        Assert.Throws<ArgumentException>(() => graph.PrepareTarget(foreign.CreateTexture(Description)));
        var wrong = new PreparedTargetTexture(new PreparedTargetDevice());
        Assert.Throws<ArgumentException>(() => graph.PrepareTarget(graph.ImportTexture(wrong, TextureState.Undefined, default)));
        Assert.False(wrong.Disposed);
        var invalid = new PreparedTargetTexture(device) { MipLevels = 2 };
        Assert.Throws<ArgumentException>(() => graph.PrepareTarget(graph.ImportTexture(invalid, TextureState.Undefined, default)));
        Assert.False(invalid.Disposed);
        var disposed = new PreparedTargetTexture(device);
        disposed.Dispose();
        Assert.Throws<ObjectDisposedException>(() => graph.PrepareTarget(graph.ImportTexture(disposed, TextureState.Undefined, default)));
    }

    /// <summary>Eager preparation does not make otherwise dead target work live.</summary>
    [Fact]
    public void PreparedTargetDoesNotBecomeCullingRoot()
    {
        var device = new PreparedTargetDevice();
        using var graph = new RenderGraph(device);
        RenderGraphTexture resource = graph.CreateTexture(Description);
        graph.PrepareTarget(resource);
        graph.AddPass("dead", _ => throw new InvalidOperationException()).UseTexture(resource, TextureState.ColorAttachment, new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite));
        using CompiledRenderGraph plan = graph.Compile();
        Assert.Equal(0, plan.Statistics.LivePasses);
        Assert.Equal(1, plan.Statistics.CulledPasses);
        Assert.True(resource.IsTransient);
    }

    /// <summary>Failed texture allocation can retry through the already linked owner.</summary>
    [Fact]
    public void AllocationFailureCanRetryWithoutReplacingOwner()
    {
        var device = new PreparedTargetDevice { FailureOnAllocation = 1 };
        using var graph = new RenderGraph(device);
        RenderGraphTexture resource = graph.CreateTexture(Description);
        Assert.Throws<InvalidOperationException>(() => graph.PrepareTarget(resource));
        RenderGraph.PreparedTarget? owner = resource.PreparedTarget;
        Assert.NotNull(owner);
        Assert.Same(owner, graph.PrepareTarget(resource));
        Assert.Equal(1, device.Textures);
    }

    /// <summary>Independent prepared ownership survives compiled plan disposal.</summary>
    [Fact]
    public void IndependentUseSurvivesCompiledPlanDisposal()
    {
        var device = new PreparedTargetDevice();
        using var graph = new RenderGraph(device);
        RenderGraph.PreparedTarget target = graph.PrepareTarget(graph.CreateTexture(Description));
        using IDisposable use = target.AcquireUse();
        IGraphicsTextureView view = target.View;
        using CompiledRenderGraph plan = graph.Compile();
        plan.Dispose();
        graph.Dispose();
        ((IGraphicsNativeOrigin)view).ValidateOrigin();
        Assert.Empty(device.Lifetime);
        use.Dispose();
        Assert.Equal(new[] { "view", "texture" }, device.Lifetime);
    }

    /// <summary>Invalid backend view results remain graph owned without consuming borrowed textures.</summary>
    [Fact]
    public void InvalidCreatedViewRemainsGraphOwned()
    {
        var device = new PreparedTargetDevice { InvalidView = true };
        var texture = new PreparedTargetTexture(device);
        var graph = new RenderGraph(device);
        RenderGraphTexture resource = graph.ImportTexture(texture, TextureState.Undefined, default);
        Assert.Throws<ArgumentException>(() => graph.PrepareTarget(resource));
        Assert.Empty(device.Lifetime);
        Assert.True(graph.IsBuilding);
        Assert.Throws<ArgumentException>(() => graph.PrepareTarget(resource));
        graph.Dispose();
        Assert.Equal(new[] { "view" }, device.Lifetime);
        Assert.False(texture.Disposed);
    }

    internal sealed class PreparedTargetTexture(PreparedTargetDevice device) : IGraphicsTexture, IGraphicsNativeOrigin
    {
        public uint Width { get; init; } = 16;

        public uint Height { get; init; } = 16;

        public uint MipLevels { get; init; } = 1;

        public uint ArrayLayers { get; init; } = 1;

        public TextureFormat Format { get; init; } = TextureFormat.Rgba8Unorm;

        public TextureUsage Usage { get; init; } = TextureUsage.RenderAttachment;

        public IGraphicDevice Device => device;

        public object AllocationIdentity => this;

        internal bool Disposed { get; private set; }

        internal bool FailDispose { get; set; }

        internal PreparedTargetView? View { get; private set; }

        public void ValidateOrigin() => ObjectDisposedException.ThrowIf(Disposed, this);

        public (uint Width, uint Height) GetMipSize(uint mipLevel) => (Width, Height);

        public IGraphicsTextureView CreateView(TextureViewDesc? desc = null)
        {
            if (device.FailView)
            {
                throw new InvalidOperationException("view creation");
            }

            return View = new PreparedTargetView(this, device);
        }

        public void Dispose()
        {
            if (FailDispose)
            {
                FailDispose = false;
                throw new InvalidOperationException("texture cleanup");
            }

            Disposed = true;
            device.Lifetime.Add("texture");
        }
    }

    internal sealed class PreparedTargetView(PreparedTargetTexture texture, PreparedTargetDevice device) : IGraphicsTextureView, IGraphicsNativeOrigin
    {
        private bool _disposed;

        public IGraphicsTexture Texture => texture;

        public IGraphicDevice Device => device;

        public object AllocationIdentity => texture;

        public TextureViewInfo Info => new() { Format = texture.Format, Dimension = TextureViewDimension.D2, BaseMipLevel = 0, MipLevelCount = device.InvalidView ? 2u : 1u, BaseArrayLayer = 0, ArrayLayerCount = 1 };

        internal bool FailDispose { get; set; }

        public void ValidateOrigin()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            texture.ValidateOrigin();
        }

        public void Dispose()
        {
            if (FailDispose)
            {
                FailDispose = false;
                throw new InvalidOperationException("view cleanup");
            }

            _disposed = true;
            device.Lifetime.Add("view");
        }
    }

    internal sealed class PreparedTargetDevice : IGraphicDevice
    {
        private int _allocations;

        public event EventHandler<GraphicsDeviceLostEventArgs>? DeviceLost
        {
            add
            {
            }

            remove
            {
            }
        }

        public GraphicsDeviceStatus Status => GraphicsDeviceStatus.Ready;

        public GraphicsDeviceLoss? Loss => null;

        public DeviceCaps Caps => new() { ShaderTarget = ShaderTarget.SpirV, MaxColorAttachments = 8 };

        public IGraphicsQueue Queue => throw new NotSupportedException();

        internal PreparedTargetTexture? LastTexture { get; private set; }

        internal bool FailView { get; set; }

        internal bool InvalidView { get; set; }

        internal List<string> Lifetime { get; } = [];

        internal int FailureOnAllocation { get; init; }

        internal Exception? AllocationException { get; init; }

        internal int Textures { get; private set; }

        internal int Buffers { get; private set; }

        public ValueTask WaitForRetirementAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException();

        public IGraphicsTexture CreateTexture(TextureDesc desc)
        {
            Allocate();
            Textures++;
            return LastTexture = new PreparedTargetTexture(this)
            {
                Width = desc.Width,
                Height = desc.Height,
                MipLevels = desc.MipLevels,
                ArrayLayers = desc.ArrayLayers,
                Format = desc.Format,
                Usage = desc.Usage,
            };
        }

        public IGraphicsBuffer<T> CreateBuffer<T>(BufferDesc<T> desc)
            where T : unmanaged
        {
            Allocate();
            Buffers++;
            return new TestTypedBuffer<T>(desc, Lifetime);
        }

        public BufferLayout<T> GetBufferLayout<T>()
            where T : unmanaged
        {
            ulong size = (ulong)System.Runtime.CompilerServices.Unsafe.SizeOf<T>();
            return new(size, size, 1, 1);
        }

        public IGraphicsSemaphore CreateSemaphore() => throw new NotSupportedException();

        public IGraphicsCommandBuffer CreateCommandBuffer(CommandBufferDesc desc) => new RecordingCommands();

        public TextureCopyLayout GetTextureCopyLayout(TextureFormat format) => throw new NotSupportedException();

        public IGraphicsSampler CreateSampler(SamplerDesc desc) => new PublicGraphConsumerSampler(desc);

        public IArgumentTable CreateArgumentTable(ArgumentTableDesc desc) => new PublicGraphConsumerTable(desc);

        public IGraphicsShader CreateShader(ShaderArtifact artifact) => throw new NotSupportedException();

        public IMeshPipeline CreateMeshPipeline(MeshPipelineDesc desc) => throw new NotSupportedException();

        public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc) => throw new NotSupportedException();

        public IGraphicsComputePipeline CreateComputePipeline(ComputePipelineDesc desc) => throw new NotSupportedException();

        public IGraphicsShaderDataBuffer<T> CreateShaderDataBuffer<T>(ShaderArtifact artifact, ulong count, MemoryPreference memory = MemoryPreference.Automatic)
            where T : struct, IShaderArguments => throw new NotSupportedException();

        private void Allocate()
        {
            if (AllocationException is { } failure)
            {
                throw failure;
            }

            if (++_allocations == FailureOnAllocation)
            {
                throw new InvalidOperationException("Simulated allocation failure.");
            }
        }
    }
}
