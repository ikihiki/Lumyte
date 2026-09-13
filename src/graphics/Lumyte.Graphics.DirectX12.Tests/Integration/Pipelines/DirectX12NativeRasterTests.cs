using System.Runtime.InteropServices;
using Lumyte.Graphics.Native;

namespace Lumyte.Graphics.DirectX12.Tests;

[Collection("GpuBackend")]
[Trait("Category", "DirectX12Conformance")]
public sealed partial class DirectX12NativeRasterTests
{
    private static readonly Lazy<byte[]> VertexCode = new(() => Compile("""
        cbuffer Root : register(b0) {
            float depth : packoffset(c0.x);
            uint vertexBase : packoffset(c0.y);
            uint expectedInstance : packoffset(c0.z);
            float red : packoffset(c15.w);
        };
        struct Output { float4 position : SV_Position; nointerpolation uint valid : TEXCOORD0; float red : TEXCOORD1; };
        Output vertexMain(uint vertex : SV_VertexID, uint instance : SV_InstanceID) {
            float2 positions[3] = {float2(-1,1),float2(3,1),float2(-1,-3)};
            Output result;
            result.position = float4(positions[(vertex-vertexBase)%3], depth, 1);
            result.valid = instance == expectedInstance;
            result.red = red;
            return result;
        }
        """, "vs_6_6", "vertexMain"));

    private static readonly Lazy<byte[]> PixelCode = new(() => Compile("""
        cbuffer Root : register(b0) { float green : packoffset(c1.x); float blue : packoffset(c1.y); };
        float4 pixelMain(float4 position : SV_Position, nointerpolation uint valid : TEXCOORD0, float red : TEXCOORD1) : SV_Target {
            return valid ? float4(red,green,blue,1) : float4(1,0,1,1);
        }
        """, "ps_6_6", "pixelMain"));

    [Fact]
    public void RasterCreationCopiesInputsAndDefersPsoUntilSubmittedWork()
    {
        using var gpu = new Fixture();
        Target color = gpu.Texture();
        byte[] vertex = VertexCode.Value.ToArray();
        byte[] pixel = PixelCode.Value.ToArray();
        NativeGpuColorTargetDescription[] colors = [new(GpuFormat.Rgba8Unorm)];
        NativeGpuRasterPipelineHandle pipeline = gpu.Pipeline(new() { ColorTargets = colors }, vertex, pixel);
        Array.Clear(vertex); Array.Clear(pixel);
        colors[0] = new(GpuFormat.D32Float);
        using NativeGpuCommandBuffer unused = gpu.Commands();
        unused.SetPipeline(pipeline);
        gpu.Submit(unused);
        Assert.Equal(0, gpu.Backend.RasterPipelineVariantCount(pipeline));
        using NativeGpuCommandBuffer draw = gpu.Commands();
        draw.DiscardTexture(color.View, GpuTextureLayout.ColorAttachment);
        draw.BeginRendering([new(color.RenderView, NativeGpuLoadOp.Clear)]);
        draw.SetPipeline(pipeline);
        byte[] root = Root(1, 0, 0);
        draw.Draw(root, 3);
        Array.Clear(root);
        draw.EndRendering();
        NativeGpuLinearRegion readback = gpu.Readback(draw, color);
        Assert.Equal(0, gpu.Backend.RasterPipelineVariantCount(pipeline));

        gpu.Submit(draw);

        Assert.Equal(new byte[] { 255, 0, 0, 255 }, Pixel(readback, 8, 8));
        Assert.Equal(1, gpu.Backend.RasterPipelineVariantCount(pipeline));
    }

    [Theory]
    [InlineData(NativeGpuIndexFormat.Uint16)]
    [InlineData(NativeGpuIndexFormat.Uint32)]
    public void IndexedDrawUsesRangeOffsetFirstIndexSignedBaseVertexAndInstance(NativeGpuIndexFormat format)
    {
        using var gpu = new Fixture();
        Target color = gpu.Texture();
        NativeGpuLinearRegion indices = gpu.Region(NativeGpuMemoryKind.CpuVisible);
        byte[] bytes = format == NativeGpuIndexFormat.Uint16
            ? MemoryMarshal.AsBytes(new ushort[] { 999, 3, 4, 5 }.AsSpan()).ToArray()
            : MemoryMarshal.AsBytes(new uint[] { 999, 3, 4, 5 }.AsSpan()).ToArray();
        Marshal.Copy(bytes, 0, indices.CpuAddress + 256, bytes.Length);
        NativeGpuRasterPipelineHandle pipeline = gpu.Pipeline();
        using NativeGpuCommandBuffer commands = gpu.Commands();
        commands.DiscardTexture(color.View, GpuTextureLayout.ColorAttachment);
        commands.BeginRendering([new(color.RenderView, NativeGpuLoadOp.Clear)]);
        commands.SetPipeline(pipeline);
        commands.DrawIndexed(Root(0, 1, 0), new(indices, 256, (ulong)bytes.Length), format,
            3, firstIndex: 1, baseVertex: -3, firstInstance: 9);
        commands.EndRendering();
        NativeGpuLinearRegion readback = gpu.Readback(commands, color);

        gpu.Submit(commands);

        Assert.Equal(new byte[] { 0, 255, 0, 255 }, Pixel(readback, 8, 8));
    }

    [Fact]
    public void BeginRenderingResetsDepthViewportAndScissor()
    {
        using var gpu = new Fixture();
        Target color = gpu.Texture();
        Target depth = gpu.Texture(GpuFormat.D32Float);
        NativeGpuRasterPipelineHandle pipeline = gpu.Pipeline(new()
            { ColorTargets = [new(GpuFormat.Rgba8Unorm)], DepthStencilFormat = GpuFormat.D32Float });
        using NativeGpuCommandBuffer commands = gpu.Commands();
        commands.DiscardTexture(color.View, GpuTextureLayout.ColorAttachment);
        commands.DiscardTexture(depth.View, GpuTextureLayout.DepthStencilWrite);
        NativeGpuColorAttachment[] attachments = [new(color.RenderView, NativeGpuLoadOp.Clear, ClearColor: new(0, 0, 1, 1))];
        commands.BeginRendering(attachments, new(depth.RenderView, NativeGpuLoadOp.Clear, NativeGpuStoreOp.Store, ClearDepth: 0));
        commands.SetPipeline(pipeline);
        commands.SetViewport(new(0, 0, 1, 1));
        commands.SetScissor(new(0, 0, 1, 1));
        commands.SetDepthStencilState(new(DepthTest: true, DepthWrite: true, DepthCompare: GpuCompareOp.Never));
        commands.Draw(Root(1, 0, 0), 3);
        commands.EndRendering();
        // This test GPU/runtime rejects the combined RT/DS access mask at command-list Close.
        // Preserve both explicit dependencies separately; the viewport/depth reset assertion is unchanged.
        // ALL also contains the synchronization scope of the preceding discard barriers.
        commands.Barrier(GpuStage.All, GpuAccess.ColorWrite, GpuStage.All, GpuAccess.ColorWrite);
        commands.Barrier(GpuStage.All, GpuAccess.DepthStencilWrite, GpuStage.All, GpuAccess.DepthStencilWrite);
        commands.BeginRendering([new(color.RenderView)], new(depth.RenderView, NativeGpuLoadOp.Load, NativeGpuStoreOp.Store));
        commands.Draw(Root(0, 1, 0), 3);
        commands.EndRendering();
        NativeGpuLinearRegion readback = gpu.Readback(commands, color);

        gpu.Submit(commands);

        Assert.Equal(new byte[] { 0, 255, 0, 255 }, Pixel(readback, 14, 14));
    }

    private static byte[] Root(float red, float green, float blue, float depth = 0.5f, uint vertexBase = 0, uint instance = 0)
    {
        var values = new uint[64];
        values[0] = BitConverter.SingleToUInt32Bits(depth); values[1] = vertexBase; values[2] = instance;
        values[4] = BitConverter.SingleToUInt32Bits(green); values[5] = BitConverter.SingleToUInt32Bits(blue);
        values[63] = BitConverter.SingleToUInt32Bits(red);
        return MemoryMarshal.AsBytes(values.AsSpan()).ToArray();
    }

    private static byte[] Pixel(NativeGpuLinearRegion readback, int x, int y)
    {
        var pixel = new byte[4];
        Marshal.Copy(readback.CpuAddress + y * 256 + x * 4, pixel, 0, 4);
        return pixel;
    }

    private static byte[] Compile(string source, string profile, string entry)
        => DirectX12NativeComputeTests.Compile(source, profile, entry);

    private sealed record Target(NativeGpuTextureHandle Texture, NativeGpuTextureView View, NativeGpuRenderViewHandle RenderView);

    private sealed class Fixture : IDisposable
    {
        private readonly List<Action> cleanup = [];
        private readonly NativeGpuDescriptorHeap resources;
        private readonly NativeGpuDescriptorHeap samplers;
        public DirectX12Backend Backend { get; } = DirectX12Backend.Create();
        public Fixture()
        {
            resources = Backend.CreateDescriptorHeap(NativeGpuDescriptorHeapKind.Resource, 8);
            cleanup.Add(() => Backend.DestroyDescriptorHeap(resources));
            samplers = Backend.CreateDescriptorHeap(NativeGpuDescriptorHeapKind.Sampler, 4);
            cleanup.Add(() => Backend.DestroyDescriptorHeap(samplers));
        }
        public NativeGpuCommandBuffer Commands()
        {
            NativeGpuCommandBuffer commands = Backend.MainQueue.StartCommandRecording();
            commands.SetResourceDescriptorHeap(resources); commands.SetSamplerDescriptorHeap(samplers);
            return commands;
        }
        public void WriteBuffer(uint index, NativeGpuRange range, NativeGpuBufferAccess access)
            => Backend.WriteBufferDescriptor(resources, index, range, access);
        public NativeGpuRasterPipelineHandle Pipeline(NativeGpuRasterPipelineDescription? description = null,
            byte[]? vertex = null, byte[]? pixel = null)
        {
            NativeGpuRasterPipelineHandle pipeline = Backend.CreateRasterPipeline(description ?? new() { ColorTargets = [new(GpuFormat.Rgba8Unorm)] },
                new(new NativeGpuShaderCode { Stage = GpuShaderStage.Vertex, Code = vertex ?? VertexCode.Value, EntryPoint = "vertexMain" },
                    new NativeGpuShaderCode { Stage = GpuShaderStage.Pixel, Code = pixel ?? PixelCode.Value, EntryPoint = "pixelMain" }));
            cleanup.Add(() => Backend.DestroyRasterPipeline(pipeline));
            return pipeline;
        }
        public NativeGpuLinearRegion Region(NativeGpuMemoryKind kind)
        {
            NativeGpuMemoryRequirements requirements = Backend.GetLinearMemoryRequirements(8192, kind);
            NativeGpuHeap heap = Backend.CreateGpuHeap(requirements.Size * 2, requirements.Alignment, kind, [requirements.Compatibility]);
            cleanup.Add(() => Backend.DestroyGpuHeap(heap));
            NativeGpuLinearRegion region = Backend.CreateLinearRegion(8192, heap, requirements.Size);
            cleanup.Add(() => Backend.DestroyLinearRegion(region));
            return region;
        }
        public Target Texture(GpuFormat format = GpuFormat.Rgba8Unorm, NativeGpuRenderViewFlags flags = NativeGpuRenderViewFlags.None)
        {
            bool depth = format is GpuFormat.D32Float or GpuFormat.Depth24PlusStencil8;
            var description = new NativeGpuTextureDescription(NativeGpuTextureDimension.TwoD, 16, 16, 1, 1, 1, 1,
                format, NativeGpuTextureUsage.CopySource | NativeGpuTextureUsage.CopyDestination
                    | (depth ? NativeGpuTextureUsage.DepthStencilAttachment : NativeGpuTextureUsage.ColorAttachment));
            NativeGpuMemoryRequirements requirements = Backend.GetTextureMemoryRequirements(description, NativeGpuMemoryKind.GpuOnly);
            NativeGpuHeap heap = Backend.CreateGpuHeap(requirements.Size * 2, requirements.Alignment,
                NativeGpuMemoryKind.GpuOnly, [requirements.Compatibility]);
            cleanup.Add(() => Backend.DestroyGpuHeap(heap));
            NativeGpuTextureHandle texture = Backend.CreateTexture(description, heap, requirements.Size);
            cleanup.Add(() => Backend.DestroyTexture(texture));
            var view = new NativeGpuTextureView(texture, NativeGpuTextureViewDimension.TwoD, format,
                format == GpuFormat.Depth24PlusStencil8 ? NativeGpuTextureAspect.DepthStencil
                    : depth ? NativeGpuTextureAspect.Depth : NativeGpuTextureAspect.Color, 0, 1, 0, 1);
            NativeGpuRenderViewHandle renderView = Backend.CreateRenderView(view, flags);
            cleanup.Add(() => Backend.DestroyRenderView(renderView));
            return new(texture, view, renderView);
        }
        public NativeGpuRenderViewHandle View(NativeGpuTextureView view, NativeGpuRenderViewFlags flags)
        {
            NativeGpuRenderViewHandle result = Backend.CreateRenderView(view, flags);
            cleanup.Add(() => Backend.DestroyRenderView(result));
            return result;
        }
        public NativeGpuLinearRegion Readback(NativeGpuCommandBuffer commands, Target texture,
            NativeGpuTextureAspect aspect = NativeGpuTextureAspect.Color)
        {
            NativeGpuLinearRegion readback = Region(NativeGpuMemoryKind.Readback);
            commands.TextureTransition(texture.View, aspect == NativeGpuTextureAspect.Color
                ? GpuTextureLayout.ColorAttachment : GpuTextureLayout.DepthStencilWrite, GpuTextureLayout.CopySource);
            commands.CopyTextureToMemory(texture.Texture, new(readback, 0, 4096),
                new(0, aspect, 0, 1, default, new(16, 16, 1), 256, 4096));
            commands.Barrier(GpuStage.Copy, GpuAccess.CopyWrite, GpuStage.Host, GpuAccess.HostRead);
            return readback;
        }
        public void Submit(NativeGpuCommandBuffer commands)
        {
            using NativeGpuSemaphore semaphore = Backend.MainQueue.CreateSemaphore(0);
            Backend.MainQueue.Submit([commands], semaphore, 1);
            commands.Dispose();
            Backend.MainQueue.Wait(semaphore, 1);
        }
        public void Dispose()
        {
            for (int index = cleanup.Count - 1; index >= 0; index--) { cleanup[index](); }
            Backend.Dispose();
        }
    }
}
