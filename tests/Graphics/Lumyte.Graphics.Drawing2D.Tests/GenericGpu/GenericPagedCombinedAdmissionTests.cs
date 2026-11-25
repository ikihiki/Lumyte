using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D.Pipelines;
using Lumyte.Graphics.Pipelines;
using Lumyte.Pipelines;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks complete paint inventory at the real admission boundary.</summary>
public sealed class GenericPagedCombinedAdmissionTests
{
    /// <summary>Fallback descriptors must reject the reported six-binding cap.</summary>
    [Fact]
    public void FallbackCombinedLimitRejects()
    {
        DeviceCaps caps = Caps(6);
        Assert.Throws<NotSupportedException>(() => PagedGpuPreparation2D.Admit(caps, 16, 4097, new()));
    }

    /// <summary>Rejection occurs before the real reserve operation and any allocator call.</summary>
    [Fact]
    public void RejectionLeavesZeroCreditAndNativeAllocations()
    {
        var device = new AdmissionDevice { Caps = Caps(6) };
        using var run = new GraphicsRun(device, null!, new(1048576, 1048576, 0, 1));
        var gpu = new GraphicsStagePreparationContext(run, null!, 0, null, new(new Dictionary<StageArtifactKey, StageArtifactLease>(), []));
        var extent = new SchemaExtent(1, new Dictionary<string, object> { ["resourceSnapshot"] = new RenderResourcesSnapshot2D([]) });
        var views = new DeviceBindings<ResourceViewsV1>(new DeviceValue(run, ResourceViewsV1.Descriptor, extent), () => { }, default);
        Assert.Throws<NotSupportedException>(() =>
        {
            PagedGpuPreparation2D.ValidateResources(gpu, views);
            PagedGpuPreparation2D.Plan plan = PagedGpuPreparation2D.Admit(device.Caps, 16, 4097, new());
            PagedGpuPreparation2D.Reserve(gpu, 16, 16, plan, views);
        });
        Assert.Equal(0, run.ReservedWorkingBytes);
        Assert.Equal(0, device.NativeAllocations);
    }

    /// <summary>Exact caps accept; one-less rejects with source counts, fallback, weights and direct spans.</summary>
    /// <param name="direct">Uses actual direct slot spans.</param>
    /// <param name="views">Source texture views.</param>
    /// <param name="samplers">Source samplers, excluding fallback.</param>
    /// <param name="textureFloor">Helper texture floor.</param>
    /// <param name="samplerFloor">Helper sampler floor.</param>
    /// <param name="bufferWeight">Buffer descriptor weight.</param>
    /// <param name="textureWeight">Texture descriptor weight.</param>
    /// <param name="samplerWeight">Sampler descriptor weight.</param>
    /// <param name="exact">Exact accepted combined cap.</param>
    [Theory]
    [InlineData(false, 0, 0, 0, 0, 1, 1, 1, 8)]
    [InlineData(false, 1, 1, 0, 0, 1, 1, 1, 9)]
    [InlineData(false, 3, 2, 0, 0, 2, 3, 5, 35)]
    [InlineData(true, 0, 0, 0, 0, 1, 1, 1, 9)]
    [InlineData(false, 0, 0, 4, 3, 1, 1, 1, 13)]
    public void WeightedInventoryHasExactBoundary(bool direct, int views, int samplers, uint textureFloor, uint samplerFloor, uint bufferWeight, uint textureWeight, uint samplerWeight, uint exact)
    {
        DeviceCaps caps = Caps(exact);
        caps = caps with { ArgumentTableBindingLimits = caps.ArgumentTableBindingLimits with { DirectSlotBindings = direct, MinimumTextureBindings = textureFloor, MinimumSamplerBindings = samplerFloor, BufferBindingWeight = bufferWeight, TextureBindingWeight = textureWeight, SamplerBindingWeight = samplerWeight } };
        Assert.Equal(2, PagedGpuPreparation2D.Admit(caps, 16, 4097, new(), views, samplers).ScenePages);
        DeviceCaps low = caps with { ArgumentTableBindingLimits = caps.ArgumentTableBindingLimits with { CombinedBindings = exact - 1 } };
        Assert.Throws<NotSupportedException>(() => PagedGpuPreparation2D.Admit(low, 16, 4097, new(), views, samplers));
    }

    /// <summary>Helper layout floors obey individual texture and sampler ceilings too.</summary>
    /// <param name="sampler">Checks the sampler rather than texture floor.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResourceHelperFloorsRejectBeforeAllocation(bool sampler)
    {
        DeviceCaps caps = Caps(100);
        caps = caps with { ArgumentTableBindingLimits = caps.ArgumentTableBindingLimits with { MinimumTextureBindings = sampler ? 0u : 4u, TexturesPerDraw = sampler ? 32u : 3u, MinimumSamplerBindings = sampler ? 4u : 0u, SamplersPerDraw = sampler ? 3u : 32u } };
        var device = new AdmissionDevice { Caps = caps };
        using var run = new GraphicsRun(device, null!, new(1048576, 1048576, 0, 1));
        var gpu = new GraphicsStagePreparationContext(run, null!, 0, null, new(new Dictionary<StageArtifactKey, StageArtifactLease>(), []));
        var extent = new SchemaExtent(1, new Dictionary<string, object> { ["resourceSnapshot"] = new RenderResourcesSnapshot2D([]) });
        var views = new DeviceBindings<ResourceViewsV1>(new DeviceValue(run, ResourceViewsV1.Descriptor, extent), () => { }, default);
        Assert.Throws<NotSupportedException>(() => PagedGpuPreparation2D.ValidateResources(gpu, views));
        Assert.Equal(0, run.ReservedWorkingBytes);
        Assert.Equal(0, device.NativeAllocations);
    }

    private static DeviceCaps Caps(uint combined) => new() { MaxBufferSize = 1UL << 30, MaxStorageBufferBindingSize = 65536, MaxStorageBuffersPerStage = 7, MaxSampledTexturesPerStage = 32, MaxSamplersPerStage = 32, MaxComputeWorkgroupsPerDimension = 65535, MaxComputeWorkgroupSizeX = 64, MaxComputeInvocationsPerWorkgroup = 64, ArgumentTableBindingLimits = new() { BufferSlots = 7, BuffersPerDraw = 7, MaximumBufferRangeBytes = 65536, ReservedBindings = 1, CombinedBindings = combined } };

    private sealed class AdmissionDevice : IGraphicDevice
    {
        public event EventHandler<GraphicsDeviceLostEventArgs>? DeviceLost
        {
            add { }
            remove { }
        }

        public GraphicsDeviceStatus Status => GraphicsDeviceStatus.Ready;

        public GraphicsDeviceLoss? Loss => null;

        public DeviceCaps Caps { get; init; } = null!;

        public IGraphicsQueue Queue => throw new NotSupportedException();

        internal int NativeAllocations { get; private set; }

        public ValueTask WaitForRetirementAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public BufferLayout<T> GetBufferLayout<T>()
            where T : unmanaged
            => throw new NotSupportedException();

        public IGraphicsBuffer<T> CreateBuffer<T>(BufferDesc<T> desc)
            where T : unmanaged
            => Fail<IGraphicsBuffer<T>>();

        public IGraphicsTexture CreateTexture(TextureDesc desc)
            => Fail<IGraphicsTexture>();

        public IGraphicsSampler CreateSampler(SamplerDesc desc)
            => Fail<IGraphicsSampler>();

        public IArgumentTable CreateArgumentTable(ArgumentTableDesc desc)
            => Fail<IArgumentTable>();

        public IGraphicsShader CreateShader(ShaderArtifact artifact)
            => Fail<IGraphicsShader>();

        public IGraphicsSemaphore CreateSemaphore()
            => Fail<IGraphicsSemaphore>();

        public IGraphicsCommandBuffer CreateCommandBuffer(CommandBufferDesc desc)
            => Fail<IGraphicsCommandBuffer>();

        public TextureCopyLayout GetTextureCopyLayout(TextureFormat format)
            => throw new NotSupportedException();

        public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc)
            => Fail<IGraphicsPipeline>();

        public IMeshPipeline CreateMeshPipeline(MeshPipelineDesc desc)
            => Fail<IMeshPipeline>();

        public IGraphicsComputePipeline CreateComputePipeline(ComputePipelineDesc desc)
            => Fail<IGraphicsComputePipeline>();

        public IGraphicsShaderDataBuffer<T> CreateShaderDataBuffer<T>(ShaderArtifact artifact, ulong count, MemoryPreference memory = MemoryPreference.Automatic)
            where T : struct, IShaderArguments
            => Fail<IGraphicsShaderDataBuffer<T>>();

        private T Fail<T>()
        {
            NativeAllocations++;
            throw new InvalidOperationException("Native allocation during CPU admission.");
        }
    }
}
