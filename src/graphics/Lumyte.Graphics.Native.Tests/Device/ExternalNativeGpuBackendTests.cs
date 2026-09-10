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

    // This project has no friend access to the contracts assembly. Backend state remains
    // private to the implementation, while callers only receive public contract types.
    private sealed class ExternalBackend(Action<object> release) : INativeGpuBackend
    {
        public GpuShaderCodeFormat ShaderCodeFormat => GpuShaderCodeFormat.SpirV;
        public NativeGpuCapabilities Capabilities => new();

        public NativeGpuMemoryRequirements GetLinearMemoryRequirements(ulong size, NativeGpuMemoryKind kind)
            => new(size, 256, new MemoryCompatibility(this, kind));

        public NativeGpuHeap CreateGpuHeap(ulong size, ulong alignment, NativeGpuMemoryKind kind,
            ReadOnlySpan<NativeGpuMemoryCompatibility> compatibilities)
        {
            MemoryCompatibility compatibility = (MemoryCompatibility)compatibilities[0];
            if (compatibility.Owner != this || compatibility.Kind != kind)
            {
                throw new ArgumentException("The requirement belongs to a different backend or memory kind.", nameof(compatibilities));
            }
            return new Heap(size, alignment, kind);
        }

        public NativeGpuLinearRegion CreateLinearRegion(ulong size, NativeGpuHeap heap, ulong offset)
            => new LinearRegion((Heap)heap, offset, size);

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
    }
}
