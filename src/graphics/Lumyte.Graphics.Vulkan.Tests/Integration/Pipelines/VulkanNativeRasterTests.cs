using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Native;

namespace Lumyte.Graphics.Vulkan.Tests;

[Collection("GpuBackend")]
public sealed unsafe partial class VulkanNativeRasterTests
{
    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void DrawUsesDirectRootSnapshotsAndDefaultViewport()
    {
        using var r = new Resources();
        var target = r.Target();
        var pipeline = r.Pipeline();
        var positions = r.Positions();
        var readback = r.Linear(256, NativeGpuMemoryKind.Readback);
        using var commands = r.Backend.MainQueue.StartCommandRecording();
        commands.SetPipeline(pipeline);
        commands.BeginRendering([new(target.View, NativeGpuLoadOp.Clear, ClearColor: new(0, 0, 0, 1))]);
        byte[] root = Root(positions.GpuAddress, new(0, 1, 0, 1), tail: 31);
        commands.SetScissor(new(0, 0, 4, 8));
        commands.Draw(root, 3);
        BinaryPrimitives.WriteSingleLittleEndian(root.AsSpan(20), 0);
        BinaryPrimitives.WriteSingleLittleEndian(root.AsSpan(24), 1);
        commands.SetScissor(new(4, 0, 4, 8));
        commands.Draw(root, 3);
        root.AsSpan().Fill(0xFF);
        commands.EndRendering();
        ReadColor(commands, target.Texture, readback);

        r.Submit(commands);

        Assert.Equal(new byte[] { 31, 255, 0, 255 }, Pixel(readback, 2, 4));
        Assert.Equal(new byte[] { 31, 0, 255, 255 }, Pixel(readback, 6, 4));
    }

    [VulkanNativeTheory]
    [Trait("Category", "VulkanNativeConformance")]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NativeIndexFetchPreservesOffsetsAndBaseVertex(bool indexed, bool wide)
    {
        using var r = new Resources();
        var target = r.Target();
        var pipeline = r.Pipeline();
        var positions = r.Positions(prefix: true);
        var indices = r.Linear(256, NativeGpuMemoryKind.CpuVisible);
        var readback = r.Linear(256, NativeGpuMemoryKind.Readback);
        if (wide) { new uint[] { 100, 2, 3, 4 }.CopyTo(MemoryMarshal.Cast<byte, uint>(Bytes(indices)[64..])); }
        else { new ushort[] { 100, 2, 3, 4 }.CopyTo(MemoryMarshal.Cast<byte, ushort>(Bytes(indices)[64..])); }
        using var commands = r.Backend.MainQueue.StartCommandRecording();
        commands.BeginRendering([new(target.View, NativeGpuLoadOp.Clear, ClearColor: new(0, 0, 0, 1))]);
        commands.SetPipeline(pipeline);
        byte[] root = Root(positions.GpuAddress, new(1, 0, 0, 1), instanceOffset: .25f);
        if (indexed)
        {
            commands.DrawIndexed(root, new(indices, 64, wide ? 16u : 8u), wide ? NativeGpuIndexFormat.Uint32 : NativeGpuIndexFormat.Uint16,
                3, firstIndex: 1, baseVertex: -1, firstInstance: 2);
        }
        else { commands.Draw(root, 3, firstVertex: 1, firstInstance: 2); }
        commands.EndRendering();
        ReadColor(commands, target.Texture, readback);

        r.Submit(commands);

        Assert.Equal(new byte[] { 0, 0, 0, 255 }, Pixel(readback, 0, 4));
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, Pixel(readback, 4, 4));
    }

    [VulkanNativeTheory]
    [Trait("Category", "VulkanNativeConformance")]
    [InlineData(false)]
    [InlineData(true)]
    public void GpuWrittenDrawArgumentsExecuteOneRecord(bool indexed)
    {
        using var r = new Resources();
        var target = r.Target();
        var pipeline = r.Pipeline();
        var producer = r.Compute(indexed ? "NativeIndexedArguments.spv" : "NativeDrawArguments.spv",
            indexed ? "indexedArgumentsMain" : "drawArgumentsMain");
        var positions = r.Positions(prefix: true);
        var arguments = r.Linear(256, NativeGpuMemoryKind.GpuOnly);
        var indices = r.Linear(256, NativeGpuMemoryKind.CpuVisible);
        new ushort[] { 100, 2, 3, 4 }.CopyTo(MemoryMarshal.Cast<byte, ushort>(Bytes(indices)[64..]));
        var readback = r.Linear(256, NativeGpuMemoryKind.Readback);
        var argumentRange = new NativeGpuRange(arguments, 64, indexed ? 20u : 16u);
        using var commands = r.Backend.MainQueue.StartCommandRecording();
        commands.SetComputePipeline(producer);
        commands.Dispatch(Root(0, default, arguments: argumentRange.GpuAddress), 1);
        commands.Barrier(GpuStage.ComputeShader, GpuAccess.ShaderWrite, GpuStage.DrawIndirect, GpuAccess.IndirectRead);
        commands.SetPipeline(pipeline);
        commands.BeginRendering([new(target.View, NativeGpuLoadOp.Clear, ClearColor: new(0, 0, 0, 1))]);
        byte[] root = Root(positions.GpuAddress, new(0, 1, 1, 1), instanceOffset: .25f);
        if (indexed) { commands.DrawIndexedIndirect(root, new(indices, 64, 8), NativeGpuIndexFormat.Uint16, argumentRange); }
        else { commands.DrawIndirect(root, argumentRange); }
        commands.EndRendering();
        ReadColor(commands, target.Texture, readback);

        r.Submit(commands);

        Assert.Equal(new byte[] { 0, 0, 0, 255 }, Pixel(readback, 0, 4));
        Assert.Equal(new byte[] { 0, 255, 255, 255 }, Pixel(readback, 4, 4));
    }

    [VulkanNativeTheory]
    [Trait("Category", "VulkanNativeConformance")]
    [InlineData(NativeGpuFrontFace.CounterClockwise, true)]
    [InlineData(NativeGpuFrontFace.Clockwise, false)]
    public void NegativeViewportPreservesUpperLeftGeometryAndFrontFace(NativeGpuFrontFace frontFace, bool visible)
    {
        using var r = new Resources();
        var target = r.Target();
        var pipeline = r.Pipeline(new() { ColorTargets = [new(GpuFormat.Rgba8Unorm)], CullMode = NativeGpuCullMode.Back, FrontFace = frontFace });
        var positions = r.Linear(256, NativeGpuMemoryKind.CpuVisible);
        new Vector2[] { new(-1, 1), new(-1, 0), new(0, 1) }.CopyTo(MemoryMarshal.Cast<byte, Vector2>(Bytes(positions)));
        var readback = r.Linear(256, NativeGpuMemoryKind.Readback);
        using var commands = r.Backend.MainQueue.StartCommandRecording();
        commands.BeginRendering([new(target.View, NativeGpuLoadOp.Clear, ClearColor: new(0, 0, 0, 1))]);
        commands.SetPipeline(pipeline);
        commands.Draw(Root(positions.GpuAddress, new(0, 1, 0, 1)), 3);
        commands.EndRendering();
        ReadColor(commands, target.Texture, readback);

        r.Submit(commands);

        Assert.Equal(visible ? new byte[] { 0, 255, 0, 255 } : [0, 0, 0, 255], Pixel(readback, 0, 0));
        Assert.Equal(new byte[] { 0, 0, 0, 255 }, Pixel(readback, 0, 7));
    }

    private static byte[] Root(ulong positions, Vector4 color, float depth = .5f, float instanceOffset = 0,
        ulong arguments = 0, uint tail = 0, uint texture = 0, uint sampler = 0)
    {
        byte[] root = new byte[80];
        BinaryPrimitives.WriteUInt64LittleEndian(root, positions);
        BinaryPrimitives.WriteUInt64LittleEndian(root.AsSpan(8), arguments);
        MemoryMarshal.Write(root.AsSpan(16), in color);
        BinaryPrimitives.WriteSingleLittleEndian(root.AsSpan(32), depth);
        BinaryPrimitives.WriteSingleLittleEndian(root.AsSpan(36), instanceOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(root.AsSpan(40), texture);
        BinaryPrimitives.WriteUInt32LittleEndian(root.AsSpan(44), sampler);
        BinaryPrimitives.WriteUInt32LittleEndian(root.AsSpan(76), tail);
        return root;
    }

    private static Span<byte> Bytes(NativeGpuLinearRegion region) => new((void*)region.CpuAddress, checked((int)region.Size));
    private static byte[] Pixel(NativeGpuLinearRegion region, int x, int y) => Bytes(region).Slice((y * 8 + x) * 4, 4).ToArray();
    private static void ReadColor(NativeGpuCommandBuffer commands, NativeGpuTextureHandle texture, NativeGpuLinearRegion readback)
    {
        commands.Barrier(GpuStage.ColorOutput, GpuAccess.ColorWrite, GpuStage.Copy, GpuAccess.CopyRead);
        commands.CopyTextureToMemory(texture, new(readback, 0, 256), new(0, NativeGpuTextureAspect.Color, 0, 1, default, new(8, 8, 1), 32, 256));
        commands.Barrier(GpuStage.Copy, GpuAccess.CopyWrite, GpuStage.Host, GpuAccess.HostRead);
    }

    private static byte[] ShaderBytes(string name)
    {
        using var source = typeof(VulkanNativeRasterTests).Assembly.GetManifestResourceStream($"Lumyte.Graphics.Vulkan.Tests.Integration.Shaders.{name}")!;
        using var memory = new MemoryStream();
        source.CopyTo(memory);
        return memory.ToArray();
    }

    private sealed partial class Resources : IDisposable
    {
        private readonly List<NativeGpuHeap> heaps = [];
        private readonly List<NativeGpuLinearRegion> regions = [];
        private readonly List<NativeGpuTextureHandle> textures = [];
        private readonly List<NativeGpuRenderViewHandle> views = [];
        private readonly List<NativeGpuDescriptorHeap> descriptors = [];
        private readonly List<NativeGpuRasterPipelineHandle> pipelines = [];
        private readonly List<NativeGpuComputePipelineHandle> compute = [];
        public VulkanBackend Backend { get; } = VulkanBackend.Create();

        public NativeGpuRasterPipelineHandle Pipeline(NativeGpuRasterPipelineDescription? description = null, string? pixel = "NativePixel.spv", string pixelEntry = "pixelMain")
        {
            List<NativeGpuShaderCode> stages = [new() { Stage = GpuShaderStage.Vertex, Code = ShaderBytes("NativeVertex.spv"), EntryPoint = "vertexMain" }];
            if (pixel is not null) { stages.Add(new() { Stage = GpuShaderStage.Pixel, Code = ShaderBytes(pixel), EntryPoint = pixelEntry }); }
            var pipeline = Backend.CreateRasterPipeline(description ?? new() { ColorTargets = [new(GpuFormat.Rgba8Unorm)] }, new(stages.ToArray()));
            pipelines.Add(pipeline);
            return pipeline;
        }
        public NativeGpuComputePipelineHandle Compute(string file, string entry)
        {
            var pipeline = Backend.CreateComputePipeline(new(new NativeGpuShaderCode { Stage = GpuShaderStage.Compute, Code = ShaderBytes(file), EntryPoint = entry }));
            compute.Add(pipeline);
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
        public NativeGpuLinearRegion Positions(bool prefix = false)
        {
            var region = Linear(256, NativeGpuMemoryKind.CpuVisible);
            Vector2[] positions = prefix ? [new(100, 100), new(-1, -1), new(3, -1), new(-1, 3)] : [new(-1, -1), new(3, -1), new(-1, 3)];
            positions.CopyTo(MemoryMarshal.Cast<byte, Vector2>(Bytes(region)));
            return region;
        }
        public (NativeGpuTextureHandle Texture, NativeGpuRenderViewHandle View) Target(GpuFormat format = GpuFormat.Rgba8Unorm,
            NativeGpuRenderViewFlags flags = NativeGpuRenderViewFlags.None)
        {
            bool depth = format is GpuFormat.D32Float or GpuFormat.Depth24PlusStencil8;
            NativeGpuTextureDescription description = new(NativeGpuTextureDimension.TwoD, 8, 8, 1, 1, 1, 1, format,
                NativeGpuTextureUsage.CopySource | NativeGpuTextureUsage.CopyDestination | NativeGpuTextureUsage.Sampled
                | (depth ? NativeGpuTextureUsage.DepthStencilAttachment : NativeGpuTextureUsage.ColorAttachment));
            var requirements = Backend.GetTextureMemoryRequirements(description, NativeGpuMemoryKind.GpuOnly);
            var heap = Backend.CreateGpuHeap(requirements.Size, requirements.Alignment, NativeGpuMemoryKind.GpuOnly, [requirements.Compatibility]);
            heaps.Add(heap);
            var texture = Backend.CreateTexture(description, heap, 0);
            textures.Add(texture);
            return (texture, View(texture, format, flags));
        }
        public NativeGpuRenderViewHandle View(NativeGpuTextureHandle texture, GpuFormat format, NativeGpuRenderViewFlags flags)
        {
            var view = Backend.CreateRenderView(new(texture, NativeGpuTextureViewDimension.TwoD, format,
                format == GpuFormat.Depth24PlusStencil8 ? NativeGpuTextureAspect.DepthStencil : format == GpuFormat.D32Float ? NativeGpuTextureAspect.Depth : NativeGpuTextureAspect.Color,
                0, 1, 0, 1), flags);
            views.Add(view);
            return view;
        }
        public NativeGpuDescriptorHeap Descriptors(NativeGpuDescriptorHeapKind kind, uint capacity)
        {
            var heap = Backend.CreateDescriptorHeap(kind, capacity);
            descriptors.Add(heap);
            return heap;
        }
        public void Submit(NativeGpuCommandBuffer commands)
        {
            var queue = Backend.MainQueue;
            using var completion = queue.CreateSemaphore(0);
            queue.Submit([commands], completion, 1);
            queue.Wait(completion, 1);
        }
        public void Dispose()
        {
            foreach (var pipeline in pipelines) { Backend.DestroyRasterPipeline(pipeline); }
            foreach (var pipeline in compute) { Backend.DestroyComputePipeline(pipeline); }
            foreach (var descriptor in descriptors) { Backend.DestroyDescriptorHeap(descriptor); }
            foreach (var view in views) { Backend.DestroyRenderView(view); }
            foreach (var texture in textures) { Backend.DestroyTexture(texture); }
            foreach (var region in regions) { Backend.DestroyLinearRegion(region); }
            foreach (var heap in heaps) { Backend.DestroyGpuHeap(heap); }
            Backend.Dispose();
        }
    }
}
