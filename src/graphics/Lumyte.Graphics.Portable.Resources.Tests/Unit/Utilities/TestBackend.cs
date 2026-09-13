namespace Lumyte.Graphics.Portable.Resources.Tests.Unit.Utilities;

internal sealed class TestBackend : IPortableGpuBackend
{
    internal sealed class Buffer(GpuBufferDescription description) : GpuBufferHandle
    { internal GpuBufferDescription Description { get; } = description; }
    internal sealed class Texture(GpuTextureDescription description) : GpuTextureHandle
    { internal GpuTextureDescription Description { get; } = description; }
    internal List<object> Created { get; } = [];
    internal List<object> Destroyed { get; } = [];
    internal Dictionary<object, Exception> DestructionErrors { get; } = new(ReferenceEqualityComparer.Instance);
    internal Exception? CreationError { get; set; }
    internal bool Disposed { get; private set; }
    public GpuBackendCapabilities Capabilities => throw new NotSupportedException();
    public GpuDeviceLimits Limits => throw new NotSupportedException();
    public IGpuQueue MainQueue => throw new NotSupportedException();

    public GpuBufferHandle CreateBuffer(GpuBufferDescription description)
    {
        if (CreationError is not null) { throw CreationError; }
        var resource = new Buffer(description);
        Created.Add(resource);
        return resource;
    }
    public GpuTextureHandle CreateTexture(GpuTextureDescription description)
    {
        if (CreationError is not null) { throw CreationError; }
        var resource = new Texture(description);
        Created.Add(resource);
        return resource;
    }
    public void DestroyBuffer(GpuBufferHandle buffer) => Destroy(buffer);
    public void DestroyTexture(GpuTextureHandle texture) => Destroy(texture);
    private void Destroy(object resource)
    {
        Destroyed.Add(resource);
        if (DestructionErrors.TryGetValue(resource, out Exception? error)) { throw error; }
    }
    public ValueTask<GpuMappedBufferRange> MapBufferAsync(GpuBufferHandle buffer, GpuMapMode mode, ulong offset, ulong length)
        => throw new NotSupportedException();
    public GpuBindingLayoutHandle CreateBindingLayout(ReadOnlySpan<GpuBindingLayoutEntry> entries) => throw new NotSupportedException();
    public void DestroyBindingLayout(GpuBindingLayoutHandle layout) => throw new NotSupportedException();
    public GpuBindingsHandle CreateBindings(GpuBindingLayoutHandle layout, ReadOnlySpan<GpuBindingEntry> entries)
        => throw new NotSupportedException();
    public void DestroyBindings(GpuBindingsHandle bindings) => throw new NotSupportedException();
    public GpuShaderModuleHandle CreateShaderModule(string wgsl) => throw new NotSupportedException();
    public void DestroyShaderModule(GpuShaderModuleHandle module) => throw new NotSupportedException();
    public GpuComputePipelineHandle CreateComputePipeline(GpuShaderProgramDescription shaders) => throw new NotSupportedException();
    public void DestroyComputePipeline(GpuComputePipelineHandle pipeline) => throw new NotSupportedException();
    public GpuRasterPipelineHandle CreateRasterPipeline(GpuRasterPipelineDescription description, GpuShaderProgramDescription shaders)
        => throw new NotSupportedException();
    public void DestroyRasterPipeline(GpuRasterPipelineHandle pipeline) => throw new NotSupportedException();
    public void Dispose() => Disposed = true;
}
