using System.Runtime.InteropServices;
using Lumyte.Graphics.Native;

namespace Lumyte.Graphics.DirectX12.Tests;

public sealed partial class DirectX12NativeRasterTests
{
    [Theory]
    [InlineData(false, NativeGpuIndexFormat.Uint32)]
    [InlineData(true, NativeGpuIndexFormat.Uint16)]
    [InlineData(true, NativeGpuIndexFormat.Uint32)]
    public void IndirectDrawUsesOneGpuWrittenNativeRecord(bool indexed, NativeGpuIndexFormat format)
    {
        using var gpu = new Fixture();
        Target color = gpu.Texture();
        NativeGpuLinearRegion arguments = gpu.Region(NativeGpuMemoryKind.GpuOnly);
        NativeGpuLinearRegion indices = gpu.Region(NativeGpuMemoryKind.CpuVisible);
        byte[] indexBytes = format == NativeGpuIndexFormat.Uint16
            ? MemoryMarshal.AsBytes(new ushort[] { 777, 3, 4, 5 }.AsSpan()).ToArray()
            : MemoryMarshal.AsBytes(new uint[] { 777, 3, 4, 5 }.AsSpan()).ToArray();
        Marshal.Copy(indexBytes, 0, indices.CpuAddress + 256, indexBytes.Length);
        byte[] writerCode = DirectX12NativeComputeTests.Compile("""
            cbuffer Root:register(b0) {uint descriptor;uint indexed;};
            [numthreads(1,1,1)] void computeMain() {
                RWByteAddressBuffer output=ResourceDescriptorHeap[descriptor];
                if(indexed) {output.Store4(128,uint4(3,1,1,asuint(-3)));output.Store(144,9);}
                else output.Store4(128,uint4(3,1,0,9));
            }
            """);
        NativeGpuComputePipelineHandle writer = gpu.Backend.CreateComputePipeline(new(new NativeGpuShaderCode
            { Stage = GpuShaderStage.Compute, Code = writerCode }));
        NativeGpuRasterPipelineHandle pipeline = gpu.Pipeline();
        gpu.WriteBuffer(2, new(arguments, 256, 256), NativeGpuBufferAccess.ReadWrite);
        try
        {
            using NativeGpuCommandBuffer commands = gpu.Commands();
            commands.SetComputePipeline(writer);
            commands.Dispatch(MemoryMarshal.AsBytes(new uint[] { 2, indexed ? 1u : 0u }.AsSpan()), 1);
            commands.Barrier(GpuStage.ComputeShader, GpuAccess.ShaderWrite, GpuStage.DrawIndirect, GpuAccess.IndirectRead);
            commands.DiscardTexture(color.View, GpuTextureLayout.ColorAttachment);
            commands.BeginRendering([new(color.RenderView, NativeGpuLoadOp.Clear)]);
            commands.SetPipeline(pipeline);
            byte[] root = Root(0, 0, 1);
            if (indexed)
            {
                commands.DrawIndexedIndirect(root, new(indices, 256, (ulong)indexBytes.Length), format, new(arguments, 384, 20));
            }
            else { commands.DrawIndirect(root, new(arguments, 384, 16)); }
            Array.Clear(root);
            commands.EndRendering();
            NativeGpuLinearRegion readback = gpu.Readback(commands, color);

            gpu.Submit(commands);

            Assert.Equal(new byte[] { 0, 0, 255, 255 }, Pixel(readback, 8, 8));
        }
        finally { gpu.Backend.DestroyComputePipeline(writer); }
    }

    [Fact]
    public void PixelShaderCanReadAndWriteCallerDescriptorsInsideRendering()
    {
        using var gpu = new Fixture();
        Target color = gpu.Texture();
        NativeGpuLinearRegion source = gpu.Region(NativeGpuMemoryKind.CpuVisible);
        NativeGpuLinearRegion destination = gpu.Region(NativeGpuMemoryKind.GpuOnly);
        NativeGpuLinearRegion result = gpu.Region(NativeGpuMemoryKind.Readback);
        Marshal.WriteInt32(source.CpuAddress + 256, 61);
        gpu.WriteBuffer(3, new(source, 256, 4), NativeGpuBufferAccess.ReadOnly);
        gpu.WriteBuffer(5, new(destination, 512, 1024), NativeGpuBufferAccess.ReadWrite);
        byte[] pixel = Compile("""
            cbuffer Root:register(b0) {uint source:packoffset(c1.z);uint destination:packoffset(c1.w);};
            float4 pixelMain(float4 position:SV_Position):SV_Target {
                ByteAddressBuffer input=ResourceDescriptorHeap[source];
                RWByteAddressBuffer output=ResourceDescriptorHeap[destination];
                uint value=input.Load(0);uint2 coordinate=(uint2)position.xy;
                output.Store((coordinate.y*16+coordinate.x)*4,value+coordinate.x);
                return float4(value/255.0,0,0,1);
            }
            """, "ps_6_6", "pixelMain");
        NativeGpuRasterPipelineHandle pipeline = gpu.Pipeline(pixel: pixel);
        byte[] root = Root(0, 0, 0);
        MemoryMarshal.Cast<byte, uint>(root.AsSpan())[6] = 3;
        MemoryMarshal.Cast<byte, uint>(root.AsSpan())[7] = 5;
        using NativeGpuCommandBuffer commands = gpu.Commands();
        commands.DiscardTexture(color.View, GpuTextureLayout.ColorAttachment);
        commands.BeginRendering([new(color.RenderView, NativeGpuLoadOp.Clear)]);
        commands.SetPipeline(pipeline);
        commands.Draw(root, 3);
        commands.EndRendering();
        NativeGpuLinearRegion pixels = gpu.Readback(commands, color);
        commands.Barrier(GpuStage.PixelShader, GpuAccess.ShaderWrite, GpuStage.Copy, GpuAccess.CopyRead);
        commands.CopyMemory(new(destination, 512, 1024), new(result, 0, 1024));
        commands.Barrier(GpuStage.Copy, GpuAccess.CopyWrite, GpuStage.Host, GpuAccess.HostRead);

        gpu.Submit(commands);

        Assert.Equal(new byte[] { 61, 0, 0, 255 }, Pixel(pixels, 8, 8));
        Assert.Equal(69, Marshal.ReadInt32(result.CpuAddress + (8 * 16 + 8) * 4));
    }

    [Fact]
    public void BlendingAndWriteMaskRemainFixedPipelineState()
    {
        using var gpu = new Fixture();
        Target color = gpu.Texture();
        byte[] pixel = Compile("""
            float4 pixelMain():SV_Target {return float4(1,0,0,.5);}
            """, "ps_6_6", "pixelMain");
        NativeGpuRasterPipelineHandle pipeline = gpu.Pipeline(new() { ColorTargets = [new(GpuFormat.Rgba8Unorm,
            GpuColorWriteMask.Red | GpuColorWriteMask.Blue,
            new(Enabled: true, SourceColorFactor: NativeGpuBlendFactor.SourceAlpha,
                DestinationColorFactor: NativeGpuBlendFactor.OneMinusSourceAlpha))] }, pixel: pixel);
        using NativeGpuCommandBuffer commands = gpu.Commands();
        commands.DiscardTexture(color.View, GpuTextureLayout.ColorAttachment);
        commands.BeginRendering([new(color.RenderView, NativeGpuLoadOp.Clear, ClearColor: new(0, 1, 1, 1))]);
        commands.SetPipeline(pipeline);
        commands.Draw(Root(0, 0, 0), 3);
        commands.EndRendering();
        NativeGpuLinearRegion readback = gpu.Readback(commands, color);

        gpu.Submit(commands);

        byte[] actual = Pixel(readback, 8, 8);
        Assert.InRange(actual[0], (byte)127, (byte)128);
        Assert.Equal(255, actual[1]);
        Assert.InRange(actual[2], (byte)127, (byte)128);
        Assert.Equal(255, actual[3]);
    }
}
