using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Native;

namespace Lumyte.Graphics.Vulkan.Tests;

[Collection("GpuBackend")]
public sealed unsafe partial class VulkanNativeComputeTests
{
    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void RawPointerDispatchCopiesEachDirectRootAtRecordTime()
    {
        using var resources = new Resources();
        var pipeline = resources.Pipeline("NativeRawCompute.spv", "rawMain");
        var output = resources.Linear(512, NativeGpuMemoryKind.GpuOnly);
        var readback = resources.Linear(512, NativeGpuMemoryKind.Readback);
        var queue = resources.Backend.MainQueue;
        using var completion = queue.CreateSemaphore(0);
        using var commands = queue.StartCommandRecording();
        byte[] root = Root(new NativeGpuRange(output, 64, 256).GpuAddress, 17, 0, tail: 700);
        commands.SetComputePipeline(pipeline);
        commands.Dispatch(root, 4);
        BinaryPrimitives.WriteUInt32LittleEndian(root.AsSpan(8), 30);
        BinaryPrimitives.WriteUInt32LittleEndian(root.AsSpan(12), 4);
        commands.Dispatch(root, 4);
        root.AsSpan().Fill(0xFF);
        Readback(commands, new(output, 64, 32), new(readback, 32, 32));

        queue.Submit([commands], completion, 1);
        queue.Wait(completion, 1);

        Assert.Equal(new uint[] { 717, 718, 719, 720, 730, 731, 732, 733 }, Words(readback)[8..16].ToArray());
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void GpuWrittenAddressArgumentsDispatchWithTheCallersRoot()
    {
        using var resources = new Resources();
        var producer = resources.Pipeline("NativeArgumentsCompute.spv", "argumentsMain");
        var consumer = resources.Pipeline("NativeRawCompute.spv", "rawMain");
        var arguments = resources.Linear(256, NativeGpuMemoryKind.GpuOnly);
        var output = resources.Linear(256, NativeGpuMemoryKind.GpuOnly);
        var readback = resources.Linear(256, NativeGpuMemoryKind.Readback);
        var argumentRange = new NativeGpuRange(arguments, 64, 12);
        var queue = resources.Backend.MainQueue;
        using var completion = queue.CreateSemaphore(0);
        using var commands = queue.StartCommandRecording();
        commands.SetComputePipeline(producer);
        commands.Dispatch(Root(argumentRange.GpuAddress, 5), 1);
        commands.Barrier(GpuStage.ComputeShader, GpuAccess.ShaderWrite, GpuStage.DrawIndirect, GpuAccess.IndirectRead);
        commands.SetComputePipeline(consumer);
        commands.DispatchIndirect(Root(new NativeGpuRange(output, 0, 256).GpuAddress, 41, tail: 9), argumentRange);
        Readback(commands, new(output, 0, 20), new(readback, 0, 20));

        queue.Submit([commands], completion, 1);
        queue.Wait(completion, 1);

        Assert.Equal(new uint[] { 50, 51, 52, 53, 54 }, Words(readback)[..5].ToArray());
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void MixedDescriptorSlotsAndPipelineSurviveTheFirstTextureSegment()
    {
        using var resources = new Resources();
        var pipeline = resources.Pipeline("NativeHeapCompute.spv", "heapMain");
        var output = resources.Linear(256, NativeGpuMemoryKind.GpuOnly);
        var readback = resources.Linear(256, NativeGpuMemoryKind.Readback);
        var upload = resources.Linear(256, NativeGpuMemoryKind.CpuVisible);
        Bytes(upload)[0] = 63;
        Bytes(upload)[1] = 127;
        Bytes(upload)[2] = 211;
        Bytes(upload)[3] = 255;
        var texture = resources.Texture();
        var resourceHeap = resources.Descriptors(NativeGpuDescriptorHeapKind.Resource, 8);
        var samplerHeap = resources.Descriptors(NativeGpuDescriptorHeapKind.Sampler, 8);
        resources.Backend.WriteBufferDescriptor(resourceHeap, 3, new(output, 64, 64), NativeGpuBufferAccess.ReadWrite);
        resources.Backend.WriteTextureDescriptor(resourceHeap, 5, View(texture));
        resources.Backend.WriteSamplerDescriptor(samplerHeap, 2, new());
        var queue = resources.Backend.MainQueue;
        using var completion = queue.CreateSemaphore(0);
        using var commands = queue.StartCommandRecording();
        commands.SetComputePipeline(pipeline);
        commands.SetResourceDescriptorHeap(resourceHeap);
        commands.SetSamplerDescriptorHeap(samplerHeap);
        commands.CopyMemoryToTexture(new(upload, 0, 4), texture,
            new(0, NativeGpuTextureAspect.Color, 0, 1, default, new(1, 1, 1), 4, 4));
        commands.Barrier(GpuStage.Copy, GpuAccess.CopyWrite, GpuStage.ComputeShader, GpuAccess.ShaderRead);
        commands.Dispatch(Root(0, 7, buffer: 3, texture: 5, sampler: 2, tail: 800), 4);
        Readback(commands, new(output, 64, 16), new(readback, 0, 16));

        queue.Submit([commands], completion, 1);
        queue.Wait(completion, 1);

        Assert.Equal(new uint[] { 870, 871, 872, 873 }, Words(readback)[..4].ToArray());
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void PipelineConsumesAnUnalignedShaderSliceBeforeReturning()
    {
        using var resources = new Resources();
        byte[] code = ShaderBytes("NativeRawCompute.spv");
        byte[] padded = new byte[code.Length + 1];
        code.CopyTo(padded, 1);
        var pipeline = resources.Pipeline(padded.AsMemory(1), "rawMain");
        padded.AsSpan().Clear();
        var output = resources.Linear(256, NativeGpuMemoryKind.GpuOnly);
        var readback = resources.Linear(256, NativeGpuMemoryKind.Readback);
        var queue = resources.Backend.MainQueue;
        using var completion = queue.CreateSemaphore(0);
        using var commands = queue.StartCommandRecording();
        commands.SetComputePipeline(pipeline);
        commands.Dispatch(Root(new NativeGpuRange(output, 0, 256).GpuAddress, 456), 1);
        Readback(commands, new(output, 0, 4), new(readback, 0, 4));

        queue.Submit([commands], completion, 1);
        queue.Wait(completion, 1);

        Assert.Equal(456u, Words(readback)[0]);
    }

    private static byte[] Root(ulong address, uint value, uint outputIndex = 0,
        uint buffer = 0, uint texture = 0, uint sampler = 0, uint tail = 0)
    {
        byte[] bytes = new byte[80];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, address);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), value);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), outputIndex);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), buffer);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20), texture);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24), sampler);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(76), tail);
        return bytes;
    }

    private static Span<byte> Bytes(NativeGpuLinearRegion region) => new((void*)region.CpuAddress, checked((int)region.Size));
    private static Span<uint> Words(NativeGpuLinearRegion region) => MemoryMarshal.Cast<byte, uint>(Bytes(region));
    private static NativeGpuTextureView View(NativeGpuTextureHandle texture) => new(texture,
        NativeGpuTextureViewDimension.TwoD, GpuFormat.Rgba8Unorm, NativeGpuTextureAspect.Color, 0, 1, 0, 1);

    private static void Readback(NativeGpuCommandBuffer commands, NativeGpuRange source, NativeGpuRange destination)
    {
        commands.Barrier(GpuStage.ComputeShader, GpuAccess.ShaderWrite, GpuStage.Copy, GpuAccess.CopyRead);
        commands.CopyMemory(source, destination);
        commands.Barrier(GpuStage.Copy, GpuAccess.CopyWrite, GpuStage.Host, GpuAccess.HostRead);
    }

    private static byte[] ShaderBytes(string name)
    {
        using var stream = typeof(VulkanNativeComputeTests).Assembly.GetManifestResourceStream(
            $"Lumyte.Graphics.Vulkan.Tests.Integration.Shaders.{name}")!;
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private sealed class Resources : IDisposable
    {
        private readonly List<NativeGpuHeap> heaps = [];
        private readonly List<NativeGpuLinearRegion> regions = [];
        private readonly List<NativeGpuTextureHandle> textures = [];
        private readonly List<NativeGpuDescriptorHeap> descriptors = [];
        private readonly List<NativeGpuComputePipelineHandle> pipelines = [];
        public VulkanBackend Backend { get; } = VulkanBackend.Create();

        public NativeGpuComputePipelineHandle Pipeline(string file, string entry) => Pipeline(ShaderBytes(file), entry);
        public NativeGpuComputePipelineHandle Pipeline(ReadOnlyMemory<byte> code, string entry)
        {
            var pipeline = Backend.CreateComputePipeline(new(new NativeGpuShaderCode { Stage = GpuShaderStage.Compute, Code = code, EntryPoint = entry }));
            pipelines.Add(pipeline);
            return pipeline;
        }
        public NativeGpuLinearRegion Linear(ulong size, NativeGpuMemoryKind kind)
        {
            var requirements = Backend.GetLinearMemoryRequirements(size, kind);
            var heap = Backend.CreateGpuHeap(requirements.Size, requirements.Alignment, kind, [requirements.Compatibility]);
            heaps.Add(heap);
            var region = Backend.CreateLinearRegion(size, heap, 0);
            regions.Add(region);
            return region;
        }
        public NativeGpuTextureHandle Texture(bool storage = false)
        {
            NativeGpuTextureDescription description = new(NativeGpuTextureDimension.TwoD, 1, 1, 1, 1, 1, 1,
                GpuFormat.Rgba8Unorm, NativeGpuTextureUsage.Sampled | NativeGpuTextureUsage.CopyDestination
                    | (storage ? NativeGpuTextureUsage.Storage | NativeGpuTextureUsage.CopySource : NativeGpuTextureUsage.None));
            var requirements = Backend.GetTextureMemoryRequirements(description, NativeGpuMemoryKind.GpuOnly);
            var heap = Backend.CreateGpuHeap(requirements.Size, requirements.Alignment, NativeGpuMemoryKind.GpuOnly, [requirements.Compatibility]);
            heaps.Add(heap);
            var texture = Backend.CreateTexture(description, heap, 0);
            textures.Add(texture);
            return texture;
        }
        public NativeGpuDescriptorHeap Descriptors(NativeGpuDescriptorHeapKind kind, uint capacity)
        {
            var heap = Backend.CreateDescriptorHeap(kind, capacity);
            descriptors.Add(heap);
            return heap;
        }
        public void Dispose()
        {
            foreach (var pipeline in pipelines) { Backend.DestroyComputePipeline(pipeline); }
            foreach (var descriptor in descriptors) { Backend.DestroyDescriptorHeap(descriptor); }
            foreach (var texture in textures) { Backend.DestroyTexture(texture); }
            foreach (var region in regions) { Backend.DestroyLinearRegion(region); }
            foreach (var heap in heaps) { Backend.DestroyGpuHeap(heap); }
            Backend.Dispose();
        }
    }
}
