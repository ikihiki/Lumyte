using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Vulkan;
using Lumyte.Pipelines;
using Xunit;

namespace Lumyte.Graphics.Pipelines.Tests;

/// <summary>Checks failed eviction after actual native destruction, including retry accounting.</summary>
public sealed class ImmutableUploadCleanupTests
{
    /// <summary>A partially destroyed owner is never a hit and stays charged until cleanup succeeds.</summary>
    /// <param name="admit">Whether whole-upload admission or allocation initiates eviction.</param>
    [NativeTheory]
    [Trait("Execution", "Gpu")]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedEvictionInvalidatesLookupAndRetainsChargesUntilRetry(bool admit)
    {
        using var native = VulkanDevice.Create();
        var device = new FaultDevice(native);
        using var cache = new ImmutableUploadCache(new(8, 1, 64));
        ImmutableUploadCache.Lease<uint> first = cache.Create<uint, Words>(1, [1u, 2u], device);
        FaultBuffer<uint> owner = Assert.IsType<FaultBuffer<uint>>(first.Buffer);
        first.Publish();
        first.Dispose();
        Assert.True(Assert.Single(cache.Snapshot().Completed));
        Assert.Throws<CleanupException>(() =>
        {
            if (admit)
            {
                cache.Admit<uint, Words>(1, [3u, 4u], 2);
            }
            else
            {
                cache.Create<uint, Words>(1, [3u, 4u], device);
            }
        });
        Assert.Equal(1, owner.NativeReleases);
        Assert.Equal(1, owner.DisposeCalls);
        Assert.Throws<ObjectDisposedException>(() => owner.ValidateByteRange(0, 4));
        Assert.Same(owner, Assert.Single(cache.Snapshot().Owners));
        Assert.Equal(8, cache.Snapshot().Bytes);
        Assert.Equal(1, cache.Snapshot().Created);
        Assert.Equal(1, device.Allocations);
        using ImmutableUploadCache.Lease<uint>? unexpected = cache.Find<uint, Words>(1, [1u, 2u]);
        Assert.Null(unexpected);
        Assert.Equal(0, cache.Snapshot().Hits);
        Assert.False(Assert.Single(cache.Snapshot().Completed));

        // Old-A admission must treat the failed owner as a miss, not retain it
        // as a matching input. Retry releases only the remaining cleanup owner.
        cache.Admit<uint, Words>(1, [1u, 2u], 2);
        Assert.Equal(2, owner.DisposeCalls);
        Assert.Equal(1, owner.NativeReleases);
        Assert.Equal(1, owner.SuccessfulCleanups);
        Assert.Empty(cache.Snapshot().Owners);
        Assert.Equal(0, cache.Snapshot().Bytes);
        Assert.Equal(1, device.Allocations);
        cache.Dispose();
        cache.Dispose();
        Assert.Equal(2, owner.DisposeCalls);
    }

    private sealed class Words : ISchema
    {
        public static SchemaDescriptor Descriptor { get; } = new(1, typeof(uint), sizeof(uint), new Dictionary<string, object>());
    }

    private sealed class CleanupException : Exception;

    private sealed class FaultBuffer<T>(IGraphicsBuffer<T> native) : IGraphicsBuffer<T>
        where T : unmanaged
    {
        public ulong SizeInBytes => native.SizeInBytes;

        public BufferLayout<T> Layout => native.Layout;

        public ulong Count => native.Count;

        public BufferUsage Usage => native.Usage;

        public MemoryPreference Memory => native.Memory;

        public bool IsMapped => native.IsMapped;

        internal int DisposeCalls { get; private set; }

        internal int NativeReleases { get; private set; }

        internal int SuccessfulCleanups { get; private set; }

        public void Dispose()
        {
            DisposeCalls++;
            if (DisposeCalls == 1)
            {
                native.Dispose();
                NativeReleases++;
                throw new CleanupException();
            }

            SuccessfulCleanups++;
        }

        public void ValidateByteRange(ulong offset, ulong length) => native.ValidateByteRange(offset, length);

        public void CopyBytesFrom(ReadOnlySpan<byte> source, ulong offset, ulong length) => native.CopyBytesFrom(source, offset, length);

        public void CopyBytesTo(Span<byte> destination, ulong offset, ulong length) => native.CopyBytesTo(destination, offset, length);

        public ValueTask MapAsync(CancellationToken cancellationToken = default) => native.MapAsync(cancellationToken);

        public void Unmap() => native.Unmap();

        public BufferSlice<T> SliceElements(ulong offset, ulong count) => native.SliceElements(offset, count);

        public void CopyFrom(ReadOnlySpan<T> source) => native.CopyFrom(source);

        public void CopyTo(Span<T> destination) => native.CopyTo(destination);
    }

    private sealed class FaultDevice(IGraphicDevice native) : IGraphicDevice
    {
        public event EventHandler<GraphicsDeviceLostEventArgs>? DeviceLost
        {
            add => native.DeviceLost += value;
            remove => native.DeviceLost -= value;
        }

        public GraphicsDeviceStatus Status => native.Status;

        public GraphicsDeviceLoss? Loss => native.Loss;

        public DeviceCaps Caps => native.Caps;

        public IGraphicsQueue Queue => native.Queue;

        internal int Allocations { get; private set; }

        public ValueTask WaitForRetirementAsync(CancellationToken cancellationToken = default) => native.WaitForRetirementAsync(cancellationToken);

        public BufferLayout<T> GetBufferLayout<T>()
            where T : unmanaged => native.GetBufferLayout<T>();

        public IGraphicsBuffer<T> CreateBuffer<T>(BufferDesc<T> desc)
            where T : unmanaged
        {
            Allocations++;
            return new FaultBuffer<T>(native.CreateBuffer(desc));
        }

        public IGraphicsTexture CreateTexture(TextureDesc desc) => native.CreateTexture(desc);

        public IGraphicsSampler CreateSampler(SamplerDesc desc) => native.CreateSampler(desc);

        public IArgumentTable CreateArgumentTable(ArgumentTableDesc desc) => native.CreateArgumentTable(desc);

        public IGraphicsShader CreateShader(ShaderArtifact artifact) => native.CreateShader(artifact);

        public IGraphicsSemaphore CreateSemaphore() => native.CreateSemaphore();

        public IGraphicsCommandBuffer CreateCommandBuffer(CommandBufferDesc desc) => native.CreateCommandBuffer(desc);

        public TextureCopyLayout GetTextureCopyLayout(TextureFormat format) => native.GetTextureCopyLayout(format);

        public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc) => native.CreateGraphicsPipeline(desc);

        public IMeshPipeline CreateMeshPipeline(MeshPipelineDesc desc) => native.CreateMeshPipeline(desc);

        public IGraphicsComputePipeline CreateComputePipeline(ComputePipelineDesc desc) => native.CreateComputePipeline(desc);

        public IGraphicsShaderDataBuffer<T> CreateShaderDataBuffer<T>(ShaderArtifact artifact, ulong count, MemoryPreference memory = MemoryPreference.Automatic)
            where T : struct, IShaderArguments => native.CreateShaderDataBuffer<T>(artifact, count, memory);
    }
}
