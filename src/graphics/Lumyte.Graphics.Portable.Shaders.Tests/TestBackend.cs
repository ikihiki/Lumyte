namespace Lumyte.Graphics.Portable.Shaders.Tests;

internal sealed class TestBackend : IPortableGpuBackend
{
    internal readonly record struct Release(string Kind, int Index);
    internal sealed class Module(string source) : GpuShaderModuleHandle
    {
        internal string Source { get; } = source;
    }
    internal sealed class Layout(int index, GpuBindingLayoutEntry[] entries) : GpuBindingLayoutHandle
    {
        internal int Index { get; } = index;
        internal GpuBindingLayoutEntry[] Entries { get; } = entries;
    }

    internal List<Module> Modules { get; } = [];
    internal List<Layout> Layouts { get; } = [];
    internal List<Release> Releases { get; } = [];
    internal Exception? ModuleCreationError { get; init; }
    internal int? FailingLayout { get; init; }
    internal Exception LayoutCreationError { get; } = new InvalidOperationException("Layout creation failed.");
    internal int? FailingLayoutRelease { get; init; }
    internal Exception LayoutReleaseError { get; } = new InvalidOperationException("Layout release failed.");
    internal bool Disposed { get; private set; }
    public GpuBackendCapabilities Capabilities { get; init; } = new(DirectRootData: true, DualSourceBlend: true);
    public GpuDeviceLimits Limits { get; init; } = new() { MaxImmediateSize = 256 };
    public IGpuQueue MainQueue => throw new NotSupportedException();

    public GpuShaderModuleHandle CreateShaderModule(string wgsl)
    {
        if (ModuleCreationError is not null) { throw ModuleCreationError; }
        var module = new Module(wgsl);
        Modules.Add(module);
        return module;
    }

    public void DestroyShaderModule(GpuShaderModuleHandle module)
    { Releases.Add(new("module", Modules.IndexOf((Module)module))); }

    public GpuBindingLayoutHandle CreateBindingLayout(ReadOnlySpan<GpuBindingLayoutEntry> entries)
    {
        if (FailingLayout == Layouts.Count) { throw LayoutCreationError; }
        var layout = new Layout(Layouts.Count, entries.ToArray());
        Layouts.Add(layout);
        return layout;
    }

    public void DestroyBindingLayout(GpuBindingLayoutHandle layout)
    {
        int index = ((Layout)layout).Index;
        Releases.Add(new("layout", index));
        if (FailingLayoutRelease == index) { throw LayoutReleaseError; }
    }

    public GpuBufferHandle CreateBuffer(GpuBufferDescription description) => throw new NotSupportedException();
    public void DestroyBuffer(GpuBufferHandle buffer) => throw new NotSupportedException();
    public ValueTask<GpuMappedBufferRange> MapBufferAsync(GpuBufferHandle buffer, GpuMapMode mode, ulong offset, ulong length)
        => throw new NotSupportedException();
    public GpuTextureHandle CreateTexture(GpuTextureDescription description) => throw new NotSupportedException();
    public void DestroyTexture(GpuTextureHandle texture) => throw new NotSupportedException();
    public GpuBindingsHandle CreateBindings(GpuBindingLayoutHandle layout, ReadOnlySpan<GpuBindingEntry> entries)
        => throw new NotSupportedException();
    public void DestroyBindings(GpuBindingsHandle bindings) => throw new NotSupportedException();
    public GpuComputePipelineHandle CreateComputePipeline(GpuShaderProgramDescription shaders) => throw new NotSupportedException();
    public void DestroyComputePipeline(GpuComputePipelineHandle pipeline) => throw new NotSupportedException();
    public GpuRasterPipelineHandle CreateRasterPipeline(GpuRasterPipelineDescription description, GpuShaderProgramDescription shaders)
        => throw new NotSupportedException();
    public void DestroyRasterPipeline(GpuRasterPipelineHandle pipeline) => throw new NotSupportedException();
    public void Dispose() => Disposed = true;
}
