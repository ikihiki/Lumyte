namespace Lumyte.Graphics.Portable.Tests.Device;

public sealed partial class ExternalPortableGpuBackendTests
{
    [Fact]
    public void ConsumerCreatesAndDestroysAnOpaqueBufferWithoutAnAllocation()
    {
        List<object> observed = [];
        using IPortableGpuBackend backend = new ExternalBackend(observed.Add);
        var description = new GpuBufferDescription((1ul << 40) + 256,
            GpuBufferUsage.Storage | GpuBufferUsage.IndirectArguments | GpuBufferUsage.CopyDestination);

        GpuBufferHandle buffer = backend.CreateBuffer(description);
        backend.DestroyBuffer(buffer);

        Assert.Collection(observed,
            value => Assert.Equal(description, Assert.IsType<BufferCreation>(value).Description),
            value => Assert.Same(buffer, value));
    }

    [Theory]
    [InlineData(GpuTextureDimension.Texture2D, 1u, 6u)]
    [InlineData(GpuTextureDimension.Texture3D, 8u, 1u)]
    public void ConsumerPassesTextureShapeAndMutableFormatToAnExternalBackend(
        GpuTextureDimension dimension, uint depth, uint layers)
    {
        List<object> observed = [];
        using IPortableGpuBackend backend = new ExternalBackend(observed.Add);
        var description = new GpuTextureDescription(dimension, 64, 32, depth, 3, layers, 1,
            GpuFormat.Rgba8Unorm, GpuTextureUsage.Sampled | GpuTextureUsage.CopyDestination, MutableFormat: true);

        GpuTextureHandle texture = backend.CreateTexture(description);
        backend.DestroyTexture(texture);

        Assert.Collection(observed,
            value => Assert.Equal(description, Assert.IsType<TextureCreation>(value).Description),
            value => Assert.Same(texture, value));
    }

    [Fact]
    public void ConsumerReadsEffectiveCapabilitiesAndFullWidthLimits()
    {
        var capabilities = new GpuBackendCapabilities(DirectRootData: true, IndirectFirstInstance: true);
        var limits = new GpuDeviceLimits
        {
            MaxBufferSize = (1ul << 40) + 256,
            MaxStorageBufferBindingSize = (1ul << 33) + 64,
            MaxImmediateSize = 128,
            MinUniformBufferOffsetAlignment = 256,
        };
        using IPortableGpuBackend backend = new ExternalBackend(_ => { }, capabilities: capabilities, limits: limits);

        var actual = (backend.Capabilities, backend.Limits);

        Assert.Equal((capabilities, limits), actual);
    }

    private sealed record BufferCreation(GpuBufferDescription Description);
    private sealed record TextureCreation(GpuTextureDescription Description);
    private sealed record MapRequest(GpuBufferHandle Buffer, GpuMapMode Mode, ulong Offset, ulong Length);
    private sealed record Unmap(GpuBufferHandle Buffer);

    // No friend access is available. This spy demonstrates the public implementation
    // boundary; runtime negotiation and GPU validation belong to backend tests.
    private sealed partial class ExternalBackend(Action<object> observe,
        Func<MapRequest, ValueTask<GpuMappedBufferRange>>? map = null,
        GpuBackendCapabilities? capabilities = null, GpuDeviceLimits? limits = null) : IPortableGpuBackend
    {
        public GpuBackendCapabilities Capabilities { get; } = capabilities ?? new(DirectRootData: true);
        public GpuDeviceLimits Limits { get; } = limits ?? new() { MaxImmediateSize = 128 };

        public GpuBufferHandle CreateBuffer(GpuBufferDescription description)
        {
            observe(new BufferCreation(description));
            return new Buffer();
        }

        public void DestroyBuffer(GpuBufferHandle buffer) => observe(buffer);

        public ValueTask<GpuMappedBufferRange> MapBufferAsync(GpuBufferHandle buffer, GpuMapMode mode, ulong offset, ulong length)
        {
            var request = new MapRequest(buffer, mode, offset, length);
            observe(request);
            return map?.Invoke(request) ?? throw new NotSupportedException("No mapping was supplied by this test.");
        }

        public GpuTextureHandle CreateTexture(GpuTextureDescription description)
        {
            observe(new TextureCreation(description));
            return new Texture();
        }

        public void DestroyTexture(GpuTextureHandle texture) => observe(texture);
        public void Dispose() { }

        private sealed class Buffer : GpuBufferHandle;
        private sealed class Texture : GpuTextureHandle;
    }
}
