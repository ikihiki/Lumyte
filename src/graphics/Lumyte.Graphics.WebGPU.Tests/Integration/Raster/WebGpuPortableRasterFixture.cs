using System.Runtime.InteropServices;
using P = Lumyte.Graphics.Portable;

namespace Lumyte.Graphics.WebGPU.Tests;

/// <summary>Explicitly owns test resources; callers await accepted work before disposal.</summary>
internal sealed class WebGpuPortableRasterFixture(WebGpuBackend backend) : IDisposable
{
    internal const string RootShader = """
        requires immediate_address_space;
        struct Root { color: u32, depth: f32 }
        var<immediate> root: Root;
        @vertex fn vertex(@builtin(vertex_index) id: u32) -> @builtin(position) vec4f {
            let positions = array<vec2f, 3>(vec2f(-1, -1), vec2f(3, -1), vec2f(-1, 3));
            return vec4f(positions[id], root.depth, 1);
        }
        @fragment fn fragment() -> @location(0) vec4f { return unpack4x8unorm(root.color); }
        """;

    internal const string SolidShader = """
        @vertex fn vertex(@builtin(vertex_index) id: u32) -> @builtin(position) vec4f {
            let positions = array<vec2f, 3>(vec2f(-1, -1), vec2f(3, -1), vec2f(-1, 3));
            return vec4f(positions[id], 0, 1);
        }
        @fragment fn fragment() -> @location(0) vec4f { return vec4f(1, 0, 0, 1); }
        """;

    private readonly List<P.GpuBufferHandle> buffers = [];
    private readonly List<P.GpuTextureHandle> textures = [];
    private readonly List<P.GpuBindingLayoutHandle> layouts = [];
    private readonly List<P.GpuBindingsHandle> bindings = [];
    private readonly List<P.GpuShaderModuleHandle> modules = [];
    private readonly List<P.GpuRasterPipelineHandle> pipelines = [];
    private readonly List<P.GpuComputePipelineHandle> computePipelines = [];
    private readonly List<P.GpuCommandBuffer> recordings = [];
    private readonly List<P.GpuSemaphore> semaphores = [];

    internal WebGpuBackend Backend { get; } = backend;
    internal P.IGpuQueue Queue => Backend.MainQueue;
    internal static async Task<WebGpuPortableRasterFixture> CreateAsync() => new(await WebGpuBackend.CreateAsync());

    internal P.GpuBufferHandle Buffer(ulong size, P.GpuBufferUsage usage)
    {
        P.GpuBufferHandle result = Backend.CreateBuffer(new(size, usage));
        buffers.Add(result);
        return result;
    }

    internal P.GpuTextureHandle Texture(uint width = 4, uint height = 4, uint mipCount = 1,
        uint layers = 1, uint samples = 1, GpuFormat format = GpuFormat.Rgba8Unorm,
        P.GpuTextureUsage usage = P.GpuTextureUsage.ColorAttachment | P.GpuTextureUsage.CopySource,
        P.GpuTextureDimension dimension = P.GpuTextureDimension.Texture2D, uint depth = 1)
    {
        P.GpuTextureHandle result = Backend.CreateTexture(new(dimension,
            width, height, depth, mipCount, layers, samples, format, usage));
        textures.Add(result);
        return result;
    }

    internal P.GpuBindingLayoutHandle Layout(params P.GpuBindingLayoutEntry[] entries)
    {
        P.GpuBindingLayoutHandle result = Backend.CreateBindingLayout(entries);
        layouts.Add(result);
        return result;
    }

    internal P.GpuBindingsHandle Bindings(P.GpuBindingLayoutHandle layout, params P.GpuBindingEntry[] entries)
    {
        P.GpuBindingsHandle result = Backend.CreateBindings(layout, entries);
        bindings.Add(result);
        return result;
    }

    internal P.GpuShaderModuleHandle Module(string source)
    {
        P.GpuShaderModuleHandle result = Backend.CreateShaderModule(source);
        modules.Add(result);
        return result;
    }

    internal P.GpuRasterPipelineHandle Pipeline(string source = SolidShader,
        P.GpuRasterPipelineDescription? description = null, uint immediateSize = 0,
        string vertex = "vertex", string fragment = "fragment", params P.GpuBindingLayoutHandle[] bindingLayouts)
    {
        P.GpuShaderModuleHandle module = Module(source);
        return Pipeline(
            description ?? new P.GpuRasterPipelineDescription([new(GpuFormat.Rgba8Unorm)]),
            new P.GpuShaderProgramDescription(
                [new(module, P.GpuShaderStage.Vertex, vertex), new(module, P.GpuShaderStage.Pixel, fragment)],
                bindingLayouts, immediateSize));
    }

    internal P.GpuRasterPipelineHandle Pipeline(P.GpuRasterPipelineDescription description, P.GpuShaderProgramDescription shaders)
    {
        P.GpuRasterPipelineHandle result = Backend.CreateRasterPipeline(description, shaders);
        pipelines.Add(result);
        return result;
    }

    internal P.GpuComputePipelineHandle ComputePipeline(string source, params P.GpuBindingLayoutHandle[] bindingLayouts)
    {
        P.GpuComputePipelineHandle result = Backend.CreateComputePipeline(new(
            [new(Module(source), P.GpuShaderStage.Compute, "main")], bindingLayouts));
        computePipelines.Add(result);
        return result;
    }

    internal P.GpuCommandBuffer Record()
    {
        P.GpuCommandBuffer result = Queue.StartCommandRecording();
        recordings.Add(result);
        return result;
    }

    internal P.GpuSemaphore Semaphore()
    {
        P.GpuSemaphore result = Queue.CreateSemaphore();
        semaphores.Add(result);
        return result;
    }

    internal async Task SubmitAndWaitAsync(P.GpuCommandBuffer commands)
    {
        P.GpuSemaphore completion = Semaphore();
        Queue.Submit([commands], completion, 1);
        await Queue.WaitAsync(completion, 1);
    }

    internal static P.GpuColorAttachment Color(P.GpuTextureHandle texture, P.GpuClearColor clear = default)
        => new(new(texture), P.GpuAttachmentLoadOperation.Clear, ClearColor: clear);

    internal P.GpuBufferHandle Readback(P.GpuCommandBuffer commands, P.GpuTextureHandle texture,
        uint width = 4, uint height = 4, uint mip = 0, uint layer = 0)
    {
        P.GpuBufferHandle result = Buffer(256 * height, P.GpuBufferUsage.MapRead | P.GpuBufferUsage.CopyDestination);
        commands.CopyTextureToBuffer(texture,
            new(mip, P.GpuTextureAspect.All, new(0, 0, layer), new(width, height, 1), 256, 256 * height), new(result));
        return result;
    }

    internal async Task<Pixel> PixelAsync(P.GpuBufferHandle readback, uint x = 1, uint y = 1)
    {
        // Map the whole row so native mapping offset alignment never constrains the chosen pixel.
        using P.GpuMappedBufferRange mapped = await Backend.MapBufferAsync(readback, P.GpuMapMode.Read, y * 256, 256);
        return MemoryMarshal.Read<Pixel>(mapped.ReadOnlyMemory.Span.Slice(checked((int)x * 4), 4));
    }

    internal async Task<P.GpuBufferHandle> UploadAsync(byte[] data)
    {
        P.GpuBufferHandle result = Buffer((ulong)data.Length, P.GpuBufferUsage.MapWrite | P.GpuBufferUsage.CopySource);
        using P.GpuMappedBufferRange mapped = await Backend.MapBufferAsync(result, P.GpuMapMode.Write, 0, (ulong)data.Length);
        data.CopyTo(mapped.Memory.Span);
        return result;
    }

    public void Dispose()
    {
        foreach (P.GpuCommandBuffer recording in recordings) { recording.Dispose(); }
        foreach (P.GpuSemaphore semaphore in semaphores) { semaphore.Dispose(); }
        foreach (P.GpuRasterPipelineHandle pipeline in pipelines) { Backend.DestroyRasterPipeline(pipeline); }
        foreach (P.GpuComputePipelineHandle pipeline in computePipelines) { Backend.DestroyComputePipeline(pipeline); }
        foreach (P.GpuBindingsHandle binding in bindings) { Backend.DestroyBindings(binding); }
        foreach (P.GpuShaderModuleHandle module in modules) { Backend.DestroyShaderModule(module); }
        foreach (P.GpuBindingLayoutHandle layout in layouts) { Backend.DestroyBindingLayout(layout); }
        foreach (P.GpuTextureHandle texture in textures) { Backend.DestroyTexture(texture); }
        foreach (P.GpuBufferHandle buffer in buffers) { Backend.DestroyBuffer(buffer); }
        Backend.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct Pixel(byte Red, byte Green, byte Blue, byte Alpha)
    {
        internal static readonly Pixel RedColor = new(255, 0, 0, 255);
        internal static readonly Pixel GreenColor = new(0, 255, 0, 255);
        internal static readonly Pixel BlueColor = new(0, 0, 255, 255);
        internal static readonly Pixel Transparent = new(0, 0, 0, 0);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct Root(uint Color, float Depth = 0);
}
