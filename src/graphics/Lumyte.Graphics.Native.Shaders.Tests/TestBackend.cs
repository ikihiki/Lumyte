namespace Lumyte.Graphics.Native.Shaders.Tests;

/// <summary>No friend access: the shader library only needs the public device description.</summary>
internal sealed class TestBackend : INativeGpuBackend
{
    public GpuShaderCodeFormat ShaderCodeFormat { get; init; } = GpuShaderCodeFormat.SpirV;
    public NativeGpuCapabilities Capabilities { get; init; }
    public NativeGpuLimits Limits { get; init; } = new(256, new(1, 1, 1, 1), ShaderFixtures.UnifiedLayout);
    internal bool Disposed { get; private set; }
    internal NativeGpuShaderProgram? CapturedProgram { get; private set; }

    public NativeGpuQueue MainQueue => throw Unexpected();
    public NativeGpuQueue? CopyQueue => throw Unexpected();
    public void Dispose() => Disposed = true;
    public NativeGpuComputePipelineHandle CreateComputePipeline(NativeGpuShaderProgram program)
    {
        CapturedProgram = program;
        return new ComputePipeline();
    }
    public void DestroyComputePipeline(NativeGpuComputePipelineHandle pipeline) { }
    public NativeGpuSemaphore CreateSemaphore(ulong initialValue = 0) => throw Unexpected();
    public NativeGpuMemoryRequirements GetLinearMemoryRequirements(ulong size, NativeGpuMemoryKind kind) => throw Unexpected();
    public NativeGpuHeap CreateGpuHeap(ulong size, ulong alignment, NativeGpuMemoryKind kind,
        ReadOnlySpan<NativeGpuMemoryCompatibility> compatibilities) => throw Unexpected();
    public void DestroyGpuHeap(NativeGpuHeap heap) => throw Unexpected();
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

    private static InvalidOperationException Unexpected() => new("Artifact loading must not perform a GPU operation.");
    private sealed class ComputePipeline : NativeGpuComputePipelineHandle;
}
