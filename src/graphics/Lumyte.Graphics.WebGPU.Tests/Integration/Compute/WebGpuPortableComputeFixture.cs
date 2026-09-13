using System.Runtime.InteropServices;
using P = Lumyte.Graphics.Portable;

namespace Lumyte.Graphics.WebGPU.Tests;

/// <summary>Owns test resources; each test explicitly awaits accepted work before fixture disposal.</summary>
internal sealed class WebGpuPortableComputeFixture(WebGpuBackend backend) : IDisposable
{
    private readonly List<P.GpuBufferHandle> buffers = [];
    private readonly List<P.GpuBindingLayoutHandle> layouts = [];
    private readonly List<P.GpuBindingsHandle> bindings = [];
    private readonly List<P.GpuShaderModuleHandle> modules = [];
    private readonly List<P.GpuComputePipelineHandle> pipelines = [];
    private readonly List<P.GpuCommandBuffer> recordings = [];
    private readonly List<P.GpuSemaphore> semaphores = [];

    internal WebGpuBackend Backend { get; } = backend;
    internal P.IGpuQueue Queue => Backend.MainQueue;

    internal static async Task<WebGpuPortableComputeFixture> CreateAsync()
        => new(await WebGpuBackend.CreateAsync());

    internal P.GpuBufferHandle Buffer(ulong size, P.GpuBufferUsage usage)
    {
        P.GpuBufferHandle buffer = Backend.CreateBuffer(new(size, usage));
        buffers.Add(buffer);
        return buffer;
    }

    internal P.GpuBindingLayoutHandle Layout(params P.GpuBindingLayoutEntry[] entries)
    {
        P.GpuBindingLayoutHandle layout = Backend.CreateBindingLayout(entries);
        layouts.Add(layout);
        return layout;
    }

    internal P.GpuBindingsHandle Bindings(P.GpuBindingLayoutHandle layout, params P.GpuBindingEntry[] entries)
    {
        P.GpuBindingsHandle result = Backend.CreateBindings(layout, entries);
        bindings.Add(result);
        return result;
    }

    internal P.GpuShaderModuleHandle Module(string wgsl)
    {
        P.GpuShaderModuleHandle module = Backend.CreateShaderModule(wgsl);
        modules.Add(module);
        return module;
    }

    internal P.GpuComputePipelineHandle Pipeline(P.GpuShaderProgramDescription description)
    {
        P.GpuComputePipelineHandle pipeline = Backend.CreateComputePipeline(description);
        pipelines.Add(pipeline);
        return pipeline;
    }

    internal P.GpuComputePipelineHandle Pipeline(string wgsl, uint immediateSize = 0,
        string entryPoint = "main", params P.GpuBindingLayoutHandle[] bindingLayouts)
        => Pipeline(new P.GpuShaderProgramDescription(
            [new(Module(wgsl), P.GpuShaderStage.Compute, entryPoint)], bindingLayouts, immediateSize));

    internal P.GpuCommandBuffer Record()
    {
        P.GpuCommandBuffer commands = Queue.StartCommandRecording();
        recordings.Add(commands);
        return commands;
    }

    internal P.GpuSemaphore Semaphore(ulong initialValue = 0)
    {
        P.GpuSemaphore semaphore = Queue.CreateSemaphore(initialValue);
        semaphores.Add(semaphore);
        return semaphore;
    }

    internal async Task SubmitAndWaitAsync(P.GpuCommandBuffer commands)
    {
        P.GpuSemaphore completion = Semaphore();
        Queue.Submit([commands], completion, 1);
        await Queue.WaitAsync(completion, 1);
    }

    internal async Task<P.GpuBufferHandle> UploadAsync(uint[] values)
    {
        byte[] data = MemoryMarshal.AsBytes(values.AsSpan()).ToArray();
        P.GpuBufferHandle buffer = Buffer((ulong)data.Length, P.GpuBufferUsage.MapWrite | P.GpuBufferUsage.CopySource);
        using P.GpuMappedBufferRange mapped = await Backend.MapBufferAsync(buffer, P.GpuMapMode.Write, 0, (ulong)data.Length);
        data.CopyTo(mapped.Memory.Span);
        return buffer;
    }

    internal async Task<uint[]> ReadAsync(P.GpuBufferHandle readback, int count, ulong offset = 0)
    {
        using P.GpuMappedBufferRange mapped = await Backend.MapBufferAsync(readback, P.GpuMapMode.Read, offset, checked((ulong)count * 4));
        return MemoryMarshal.Cast<byte, uint>(mapped.ReadOnlyMemory.Span).ToArray();
    }

    public void Dispose()
    {
        foreach (P.GpuCommandBuffer recording in recordings) { recording.Dispose(); }
        foreach (P.GpuSemaphore semaphore in semaphores) { semaphore.Dispose(); }
        foreach (P.GpuComputePipelineHandle pipeline in pipelines) { Backend.DestroyComputePipeline(pipeline); }
        foreach (P.GpuBindingsHandle binding in bindings) { Backend.DestroyBindings(binding); }
        foreach (P.GpuShaderModuleHandle module in modules) { Backend.DestroyShaderModule(module); }
        foreach (P.GpuBindingLayoutHandle layout in layouts) { Backend.DestroyBindingLayout(layout); }
        foreach (P.GpuBufferHandle buffer in buffers) { Backend.DestroyBuffer(buffer); }
        Backend.Dispose();
    }
}
