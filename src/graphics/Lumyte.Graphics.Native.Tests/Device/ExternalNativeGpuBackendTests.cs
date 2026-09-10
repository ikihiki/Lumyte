namespace Lumyte.Graphics.Native.Tests.Device;

public sealed class ExternalNativeGpuBackendTests
{
    [Fact]
    public void ConsumerUsesPrivateBackendHandlesThroughPublicContracts()
    {
        List<object> released = [];
        using INativeGpuBackend backend = new ExternalBackend(released.Add);
        const ulong logicalSize = 512;
        NativeGpuMemoryRequirements requirements = backend.GetLinearMemoryRequirements(logicalSize, NativeGpuMemoryKind.GpuOnly);
        NativeGpuHeap heap = backend.CreateGpuHeap(
            requirements.Size * 2, requirements.Alignment, NativeGpuMemoryKind.GpuOnly, [requirements.Compatibility]);
        NativeGpuLinearRegion region = backend.CreateLinearRegion(logicalSize, heap, requirements.Alignment);

        NativeGpuRange data = new NativeGpuRange(region, 128, 64).Slice(16, 32);
        var actual = (data.GpuAddress, data.Size);
        backend.DestroyLinearRegion(data.Region);
        backend.DestroyGpuHeap(region.Heap);

        Assert.Equal((0x8090ul, 32ul), actual);
        Assert.Collection(released,
            value => Assert.Same(region, value),
            value => Assert.Same(heap, value));
    }

    [Fact]
    public void ConsumerPlacesAnOpaqueTextureAndLinearRegionInOneHeap()
    {
        List<object> observed = [];
        using INativeGpuBackend backend = new ExternalBackend(observed.Add, observed.Add);
        var description = new NativeGpuTextureDescription(
            NativeGpuTextureDimension.TwoD, 64, 32, 1, 3, 6, 1, GpuFormat.Rgba8Unorm,
            NativeGpuTextureUsage.Sampled | NativeGpuTextureUsage.Storage
                | NativeGpuTextureUsage.CopySource | NativeGpuTextureUsage.CopyDestination,
            MutableFormat: true);
        const NativeGpuMemoryKind kind = NativeGpuMemoryKind.GpuOnly;
        NativeGpuMemoryRequirements linearRequirements = backend.GetLinearMemoryRequirements(4096, kind);
        NativeGpuMemoryRequirements textureRequirements = backend.GetTextureMemoryRequirements(description, kind);
        ulong alignment = Math.Max(linearRequirements.Alignment, textureRequirements.Alignment);
        ulong textureOffset = (linearRequirements.Size + alignment - 1) / alignment * alignment;
        NativeGpuHeap heap = backend.CreateGpuHeap(textureOffset + textureRequirements.Size, alignment, kind,
            [linearRequirements.Compatibility, textureRequirements.Compatibility]);

        NativeGpuLinearRegion region = backend.CreateLinearRegion(4096, heap, 0);
        NativeGpuTextureHandle texture = backend.CreateTexture(description, heap, textureOffset);
        backend.DestroyTexture(texture);
        backend.DestroyLinearRegion(region);
        backend.DestroyGpuHeap(heap);

        Assert.Collection(observed,
            value => Assert.Equal(new TextureMemoryRequest(description, kind), Assert.IsType<TextureMemoryRequest>(value)),
            value => Assert.Equal(new TexturePlacement(description, heap, textureOffset), Assert.IsType<TexturePlacement>(value)),
            value => Assert.Same(texture, value),
            value => Assert.Same(region, value),
            value => Assert.Same(heap, value));
    }

    private sealed record TextureMemoryRequest(NativeGpuTextureDescription Description, NativeGpuMemoryKind Kind);

    private sealed record TexturePlacement(NativeGpuTextureDescription Description, NativeGpuHeap Heap, ulong Offset);

    // This project has no friend access to the contracts assembly. Backend state remains
    // private to the implementation, while callers only receive public contract types.
    private sealed class ExternalBackend(Action<object> release, Action<object>? observeTexture = null) : INativeGpuBackend
    {
        public GpuShaderCodeFormat ShaderCodeFormat => GpuShaderCodeFormat.SpirV;
        public NativeGpuCapabilities Capabilities => new();

        public NativeGpuMemoryRequirements GetLinearMemoryRequirements(ulong size, NativeGpuMemoryKind kind)
            => new(size, 256, new MemoryCompatibility(this, kind));

        public NativeGpuHeap CreateGpuHeap(ulong size, ulong alignment, NativeGpuMemoryKind kind,
            ReadOnlySpan<NativeGpuMemoryCompatibility> compatibilities)
        {
            foreach (NativeGpuMemoryCompatibility requirement in compatibilities)
            {
                MemoryCompatibility compatibility = (MemoryCompatibility)requirement;
                if (compatibility.Owner != this || compatibility.Kind != kind)
                {
                    throw new ArgumentException("The requirement belongs to a different backend or memory kind.", nameof(compatibilities));
                }
            }
            return new Heap(size, alignment, kind);
        }

        public NativeGpuLinearRegion CreateLinearRegion(ulong size, NativeGpuHeap heap, ulong offset)
            => new LinearRegion((Heap)heap, offset, size);

        public NativeGpuMemoryRequirements GetTextureMemoryRequirements(NativeGpuTextureDescription description, NativeGpuMemoryKind kind)
        {
            observeTexture?.Invoke(new TextureMemoryRequest(description, kind));
            return new(65536, 65536, new MemoryCompatibility(this, kind));
        }

        public NativeGpuTextureHandle CreateTexture(NativeGpuTextureDescription description, NativeGpuHeap heap, ulong offset)
        {
            var placement = new TexturePlacement(description, (Heap)heap, offset);
            observeTexture?.Invoke(placement);
            return new Texture(placement);
        }

        public void DestroyTexture(NativeGpuTextureHandle texture) => release((Texture)texture);

        public void DestroyLinearRegion(NativeGpuLinearRegion region) => release((LinearRegion)region);
        public void DestroyGpuHeap(NativeGpuHeap heap) => release((Heap)heap);
        public void Dispose() { }

        private sealed class MemoryCompatibility(ExternalBackend owner, NativeGpuMemoryKind kind)
            : NativeGpuMemoryCompatibility
        {
            public ExternalBackend Owner { get; } = owner;
            public NativeGpuMemoryKind Kind { get; } = kind;
        }

        private sealed class Heap(ulong size, ulong alignment, NativeGpuMemoryKind kind)
            : NativeGpuHeap(size, alignment, kind);

        private sealed class LinearRegion(Heap heap, ulong offset, ulong size)
            : NativeGpuLinearRegion(heap, offset, size, 0x8000, 0);

        private sealed class Texture(TexturePlacement placement) : NativeGpuTextureHandle
        {
            public TexturePlacement Placement { get; } = placement;
        }
    }
}
