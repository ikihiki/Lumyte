using System.Runtime.Versioning;
using Lumyte.Graphics.WebGPU.Browser;
using P = Lumyte.Graphics.Portable;

[SupportedOSPlatform("browser")]
internal sealed class BrowserCaseFixture : IDisposable
{
    private readonly WebGpuBrowserRuntime runtime;
    private readonly List<Action> cleanup = [];

    private BrowserCaseFixture(WebGpuBrowserRuntime runtime, P.IPortableGpuBackend backend)
    {
        this.runtime = runtime;
        Backend = backend;
    }

    public P.IPortableGpuBackend Backend { get; }
    public P.IGpuQueue Queue => Backend.MainQueue;

    public static async Task<BrowserCaseFixture> CreateAsync()
    {
        WebGpuBrowserRuntime runtime = await WebGpuBrowserRuntime.LoadAsync("/lumyte-webgpu.js");
        try { return new(runtime, await WebGpuBackend.CreateAsync(runtime)); }
        catch { runtime.Dispose(); throw; }
    }

    public P.GpuBufferHandle Buffer(ulong size, P.GpuBufferUsage usage)
    {
        P.GpuBufferHandle value = Backend.CreateBuffer(new(size, usage));
        cleanup.Add(() => Backend.DestroyBuffer(value));
        return value;
    }

    public P.GpuTextureHandle Texture(uint width = 1, uint height = 1)
    {
        P.GpuTextureHandle value = Backend.CreateTexture(new(P.GpuTextureDimension.Texture2D, width, height, 1, 1, 1, 1,
            Lumyte.Graphics.GpuFormat.Rgba8Unorm, P.GpuTextureUsage.Sampled | P.GpuTextureUsage.ColorAttachment | P.GpuTextureUsage.CopySource | P.GpuTextureUsage.CopyDestination));
        cleanup.Add(() => Backend.DestroyTexture(value));
        return value;
    }

    public P.GpuBindingLayoutHandle Layout(params P.GpuBindingLayoutEntry[] entries)
    {
        P.GpuBindingLayoutHandle value = Backend.CreateBindingLayout(entries.Length == 0 ? [
            new(0, P.GpuShaderStage.Compute, new P.GpuBufferBindingLayout(P.GpuBufferBindingType.Storage))] : entries);
        cleanup.Add(() => Backend.DestroyBindingLayout(value));
        return value;
    }

    public P.GpuBindingsHandle Bindings(P.GpuBindingLayoutHandle layout, P.GpuBufferHandle buffer)
        => BindingEntries(layout, P.GpuBindingEntry.Buffer(0, new(buffer)));

    public P.GpuBindingsHandle BindingEntries(P.GpuBindingLayoutHandle layout, params P.GpuBindingEntry[] entries)
    {
        P.GpuBindingsHandle value = Backend.CreateBindings(layout, entries);
        cleanup.Add(() => Backend.DestroyBindings(value));
        return value;
    }

    public P.GpuShaderModuleHandle Module(string source)
    {
        P.GpuShaderModuleHandle value = Backend.CreateShaderModule(source);
        cleanup.Add(() => Backend.DestroyShaderModule(value));
        return value;
    }

    public P.GpuComputePipelineHandle Compute(string source, uint immediateSize = 0, string entry = "main", params P.GpuBindingLayoutHandle[] layouts)
    {
        P.GpuShaderModuleHandle module = Module(source);
        P.GpuComputePipelineHandle value = Backend.CreateComputePipeline(new([new(module, P.GpuShaderStage.Compute, entry)], layouts, immediateSize));
        cleanup.Add(() => Backend.DestroyComputePipeline(value));
        return value;
    }

    public P.GpuRasterPipelineHandle Raster(string source, uint immediateSize, params P.GpuBindingLayoutHandle[] layouts)
    {
        P.GpuShaderModuleHandle module = Module(source);
        P.GpuRasterPipelineHandle value = Backend.CreateRasterPipeline(new([new(Lumyte.Graphics.GpuFormat.Rgba8Unorm)]),
            new([new(module, P.GpuShaderStage.Vertex, "vertex"), new(module, P.GpuShaderStage.Pixel, "fragment")], layouts, immediateSize));
        cleanup.Add(() => Backend.DestroyRasterPipeline(value));
        return value;
    }

    public P.GpuCommandBuffer Record()
    {
        P.GpuCommandBuffer value = Queue.StartCommandRecording();
        cleanup.Add(value.Dispose);
        return value;
    }

    public P.GpuSemaphore Semaphore(ulong initial = 0)
    {
        P.GpuSemaphore value = Queue.CreateSemaphore(initial);
        cleanup.Add(value.Dispose);
        return value;
    }

    public async Task SubmitAsync(P.GpuCommandBuffer commands)
    {
        P.GpuSemaphore completion = Semaphore();
        Queue.Submit([commands], completion, 1);
        await Queue.WaitAsync(completion, 1);
    }

    public async Task<P.GpuBufferHandle> UploadAsync(byte[] bytes)
    {
        P.GpuBufferHandle buffer = Buffer((ulong)bytes.Length, P.GpuBufferUsage.MapWrite | P.GpuBufferUsage.CopySource);
        using P.GpuMappedBufferRange mapped = await Backend.MapBufferAsync(buffer, P.GpuMapMode.Write, 0, (ulong)bytes.Length);
        bytes.CopyTo(mapped.Memory);
        return buffer;
    }

    public async Task<byte[]> ReadAsync(P.GpuBufferHandle buffer, ulong size)
    {
        using P.GpuMappedBufferRange mapped = await Backend.MapBufferAsync(buffer, P.GpuMapMode.Read, 0, size);
        return mapped.ReadOnlyMemory.ToArray();
    }

    public void Dispose()
    {
        // Every case ends all submitted work before leaving its fixture, including expected validation failures.
        for (int i = cleanup.Count - 1; i >= 0; --i) { cleanup[i](); }
        Backend.Dispose();
        runtime.Dispose();
    }
}
