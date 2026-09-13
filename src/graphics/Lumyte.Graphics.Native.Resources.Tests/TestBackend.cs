namespace Lumyte.Graphics.Native.Resources.Tests;

internal sealed class TestBackend : INativeGpuBackend
{
    internal List<Creation> Creations { get; } = [];
    internal List<NativeGpuHeap> Destroyed { get; } = [];
    internal Exception? CreationError { get; set; }
    internal Func<NativeGpuHeap, Exception?>? DestructionError { get; set; }
    internal bool Disposed { get; private set; }

    public NativeGpuHeap CreateGpuHeap(ulong size, ulong alignment, NativeGpuMemoryKind kind,
        ReadOnlySpan<NativeGpuMemoryCompatibility> compatibilities)
    {
        if (CreationError is { } error) { throw error; }
        Heap heap = new(size, alignment, kind);
        Creations.Add(new(heap, compatibilities.ToArray()));
        return heap;
    }
    public void DestroyGpuHeap(NativeGpuHeap heap)
    {
        Destroyed.Add(heap);
        if (DestructionError?.Invoke(heap) is { } error) { throw error; }
    }
    public void Dispose() => Disposed = true;

    // A token whose user-defined equality cannot safely be consulted by the arena.
    internal sealed class Compatibility : NativeGpuMemoryCompatibility
    {
        public override bool Equals(object? obj) => throw new InvalidOperationException("Opaque token equality was invoked.");
        public override int GetHashCode() => throw new InvalidOperationException("Opaque token hashing was invoked.");
    }
    internal sealed class Heap(ulong size, ulong alignment, NativeGpuMemoryKind kind) : NativeGpuHeap(size, alignment, kind);
    internal sealed record Creation(NativeGpuHeap Heap, NativeGpuMemoryCompatibility[] Compatibilities);

    public GpuShaderCodeFormat ShaderCodeFormat => throw Unexpected();
    public NativeGpuCapabilities Capabilities => throw Unexpected();
    public NativeGpuLimits Limits => throw Unexpected();
    public NativeGpuQueue MainQueue => throw Unexpected();
    public NativeGpuQueue? CopyQueue => throw Unexpected();
    public NativeGpuSemaphore CreateSemaphore(ulong initialValue = 0) => throw Unexpected();
    public NativeGpuMemoryRequirements GetLinearMemoryRequirements(ulong size, NativeGpuMemoryKind kind) => throw Unexpected();
    public NativeGpuLinearRegion CreateLinearRegion(ulong size, NativeGpuHeap heap, ulong offset) => throw Unexpected();
    public void DestroyLinearRegion(NativeGpuLinearRegion region) => throw Unexpected();
    public NativeGpuMemoryRequirements GetTextureMemoryRequirements(NativeGpuTextureDescription description, NativeGpuMemoryKind kind) => throw Unexpected();
    public NativeGpuTextureHandle CreateTexture(NativeGpuTextureDescription description, NativeGpuHeap heap, ulong offset) => throw Unexpected();
    public void DestroyTexture(NativeGpuTextureHandle texture) => throw Unexpected();
    public NativeGpuRenderViewHandle CreateRenderView(NativeGpuTextureView view, NativeGpuRenderViewFlags flags = NativeGpuRenderViewFlags.None) => throw Unexpected();
    public void DestroyRenderView(NativeGpuRenderViewHandle view) => throw Unexpected();
    public NativeGpuDescriptorHeap CreateDescriptorHeap(NativeGpuDescriptorHeapKind kind, uint capacity) => throw Unexpected();
    public void DestroyDescriptorHeap(NativeGpuDescriptorHeap heap) => throw Unexpected();
    public void WriteTextureDescriptor(NativeGpuDescriptorHeap heap, uint index, NativeGpuTextureView view,
        NativeGpuTextureDescriptorType type = NativeGpuTextureDescriptorType.Sampled) => throw Unexpected();
    public void WriteBufferDescriptor(NativeGpuDescriptorHeap heap, uint index, NativeGpuRange range, NativeGpuBufferAccess access) => throw Unexpected();
    public void WriteSamplerDescriptor(NativeGpuDescriptorHeap heap, uint index, NativeGpuSamplerDescription description) => throw Unexpected();
    public NativeGpuRasterPipelineHandle CreateRasterPipeline(NativeGpuRasterPipelineDescription description, NativeGpuShaderProgram program) => throw Unexpected();
    public void DestroyRasterPipeline(NativeGpuRasterPipelineHandle pipeline) => throw Unexpected();
    public NativeGpuComputePipelineHandle CreateComputePipeline(NativeGpuShaderProgram program) => throw Unexpected();
    public void DestroyComputePipeline(NativeGpuComputePipelineHandle pipeline) => throw Unexpected();
    private static InvalidOperationException Unexpected() => new("The arena must only create or destroy backing heaps.");
}
