using System.Runtime.InteropServices;
using Lumyte.Graphics.Native;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;
using Feature = Silk.NET.Direct3D12.Feature;

namespace Lumyte.Graphics.DirectX12.Tests;

public sealed partial class DirectX12NativeRasterTests
{
    [Theory]
    [Trait("Requires", "ShaderModel68ExtendedCommandInfo")]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DrawForwardsNativeStartAndBaseArguments(bool indexed, bool indirect)
    {
        RequireExtendedCommandInfo();
        using var gpu = new Fixture();
        Target color = gpu.Texture();
        NativeGpuLinearRegion input = gpu.Region(NativeGpuMemoryKind.CpuVisible);
        NativeGpuLinearRegion output = gpu.Region(NativeGpuMemoryKind.GpuOnly);
        NativeGpuLinearRegion readback = gpu.Region(NativeGpuMemoryKind.Readback);
        // These optional SM6.8 semantics make API arguments observable without injecting
        // offsets into production root data or relying on SV_InstanceID including a base.
        byte[] vertex = Compile("""
            cbuffer Root:register(b0) {uint descriptor;};
            float4 vertexMain(uint vertex:SV_VertexID,uint instance:SV_InstanceID,
                int baseVertex:SV_StartVertexLocation,uint firstInstance:SV_StartInstanceLocation):SV_Position {
                RWByteAddressBuffer output=ResourceDescriptorHeap[descriptor];
                uint unused;
                output.InterlockedExchange(0,asuint(baseVertex),unused);
                output.InterlockedExchange(4,firstInstance,unused);
                output.InterlockedExchange(8,instance,unused);
                float2 positions[3]={float2(-1,1),float2(3,1),float2(-1,-3)};
                return float4(positions[vertex%3],.5,1);
            }
            """, "vs_6_8", "vertexMain");
        byte[] pixel = Compile("float4 pixelMain():SV_Target{return float4(0,1,0,1);}", "ps_6_6", "pixelMain");
        NativeGpuRasterPipelineHandle pipeline = gpu.Pipeline(vertex: vertex, pixel: pixel);
        gpu.WriteBuffer(4, new(output, 512, 16), NativeGpuBufferAccess.ReadWrite);
        uint[] indices = [777, 3, 4, 5];
        byte[] bytes = MemoryMarshal.AsBytes(indices.AsSpan()).ToArray();
        Marshal.Copy(bytes, 0, input.CpuAddress + 256, bytes.Length);
        uint[] arguments = indexed ? [3, 1, 1, unchecked((uint)-3), 13] : [3, 1, 6, 13];
        bytes = MemoryMarshal.AsBytes(arguments.AsSpan()).ToArray();
        Marshal.Copy(bytes, 0, input.CpuAddress + 512, bytes.Length);
        using NativeGpuCommandBuffer commands = gpu.Commands();
        commands.DiscardTexture(color.View, GpuTextureLayout.ColorAttachment);
        commands.BeginRendering([new(color.RenderView, NativeGpuLoadOp.Clear)]);
        commands.SetPipeline(pipeline);
        byte[] root = BitConverter.GetBytes(4u);
        if (indexed && indirect) { commands.DrawIndexedIndirect(root, new(input, 256, 16), NativeGpuIndexFormat.Uint32, new(input, 512, 20)); }
        else if (indexed) { commands.DrawIndexed(root, new(input, 256, 16), NativeGpuIndexFormat.Uint32, 3, firstIndex: 1, baseVertex: -3, firstInstance: 13); }
        else if (indirect) { commands.DrawIndirect(root, new(input, 512, 16)); }
        else { commands.Draw(root, 3, firstVertex: 6, firstInstance: 13); }
        commands.EndRendering();
        commands.Barrier(GpuStage.VertexShader, GpuAccess.ShaderWrite, GpuStage.Copy, GpuAccess.CopyRead);
        commands.CopyMemory(new(output, 512, 12), new(readback, 0, 12));
        commands.Barrier(GpuStage.Copy, GpuAccess.CopyWrite, GpuStage.Host, GpuAccess.HostRead);

        gpu.Submit(commands);

        var actual = new int[3];
        Marshal.Copy(readback.CpuAddress, actual, 0, actual.Length);
        Assert.Equal(new int[] { indexed ? -3 : 6, 13, 0 }, actual);
    }

    private static unsafe void RequireExtendedCommandInfo()
    {
        using D3D12 api = D3D12.GetApi();
        ComPtr<ID3D12Device> device = default;
        try
        {
            Assert.True(api.CreateDevice<IDXGIAdapter, ID3D12Device>(default, D3DFeatureLevel.Level110, out device) >= 0,
                "The optional extended-command conformance test requires a Direct3D 12 device.");
            var model = new FeatureDataShaderModel { HighestShaderModel = D3DShaderModel.ShaderModel68 };
            Assert.True(device.CheckFeatureSupport(Feature.ShaderModel, &model, (uint)sizeof(FeatureDataShaderModel)) >= 0
                && model.HighestShaderModel >= D3DShaderModel.ShaderModel68, "This isolated test requires Shader Model 6.8.");
            var options = new FeatureDataD3D12Options21();
            Assert.True(device.CheckFeatureSupport(Feature.D3D12Options21, &options, (uint)sizeof(FeatureDataD3D12Options21)) >= 0
                && options.ExtendedCommandInfoSupported, "This isolated test requires OPTIONS21 ExtendedCommandInfoSupported.");
        }
        finally { device.Dispose(); }
    }
}
