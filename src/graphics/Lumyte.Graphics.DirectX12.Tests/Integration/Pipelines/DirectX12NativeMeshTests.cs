using System.Runtime.InteropServices;
using Lumyte.Graphics.Native;
using Fixture = Lumyte.Graphics.DirectX12.Tests.DirectX12NativeRasterTests.Fixture;
using static Lumyte.Graphics.DirectX12.Tests.DirectX12NativeRasterTests;

namespace Lumyte.Graphics.DirectX12.Tests;

[Collection("GpuBackend")]
[Trait("Category", "DirectX12Conformance")]
[Trait("Requires", "MeshShaders")]
public sealed partial class DirectX12NativeMeshTests
{
    private const string RootSource = """
        cbuffer Root:register(b0) {
            float depth:packoffset(c0.x); uint descriptor:packoffset(c0.y);
            float green:packoffset(c1.x); float blue:packoffset(c1.y); float red:packoffset(c15.w);
        };
        struct Output {float4 position:SV_Position;float2 color:TEXCOORD0;};
        struct Payload {float red;};
        """;
    private static readonly Lazy<byte[]> Mesh = new(() => Compile(MeshSource(false), "ms_6_6", "meshMain"));
    private static readonly Lazy<byte[]> PayloadMesh = new(() => Compile(MeshSource(true), "ms_6_6", "meshMain"));
    private static readonly Lazy<byte[]> Amplification = new(() => Compile(AmplificationSource(), "as_6_6", "amplificationMain"));
    private static readonly Lazy<byte[]> PixelShader = new(() => Compile(RootSource + """
        float4 pixelMain(Output value):SV_Target {return float4(value.color,blue,1);}
        """, "ps_6_6", "pixelMain"));

    private static string MeshSource(bool payload, string extra = "") => RootSource + """
        [outputtopology("triangle")][numthreads(1,1,1)]
        void meshMain(
        """ + (payload ? "in payload Payload payload," : "") + """
            uint3 group:SV_GroupID,out vertices Output vertices[3],out indices uint3 indices[1]) {
            SetMeshOutputCounts(3,1);
        """ + extra + """
            float2 positions[3]={float2(-1,1),float2(3,1),float2(-1,-3)};
            for(uint i=0;i<3;i++) {
                vertices[i].position=float4(positions[i],depth,1);
                vertices[i].color=float2(
        """ + (payload ? "payload.red" : "red") + """
                    ,green);
            }
            indices[0]=uint3(0,1,2);
        }
        """;

    private static string AmplificationSource(string extra = "") => RootSource + """
        groupshared Payload outputPayload;
        [numthreads(1,1,1)] void amplificationMain(uint3 group:SV_GroupID) {
        """ + extra + """
            outputPayload.red=red;
            DispatchMesh(1,1,1,outputPayload);
        }
        """;

    private static byte[] Root(float red = 1, float green = 0, float blue = 0, float depth = .5f, uint descriptor = 0)
    {
        var values = new uint[64];
        values[0] = BitConverter.SingleToUInt32Bits(depth);
        values[1] = descriptor;
        values[4] = BitConverter.SingleToUInt32Bits(green);
        values[5] = BitConverter.SingleToUInt32Bits(blue);
        values[63] = BitConverter.SingleToUInt32Bits(red);
        return MemoryMarshal.AsBytes(values.AsSpan()).ToArray();
    }

    private static byte[] Compile(string source, string profile, string entry)
        => DirectX12NativeComputeTests.Compile(source, profile, entry);

    private static void RequireMesh(Fixture gpu)
    {
        Assert.True(gpu.Backend.Capabilities.MeshShaders,
            "This conformance test requires a Direct3D 12 mesh shader device.");
        Assert.True(gpu.Backend.Capabilities.AmplificationShaders);
        Assert.NotNull(gpu.Backend.Limits.MeshShader);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MeshCreationSnapshotsEveryStageAndDefersPsoUntilSubmittedWork(bool amplification)
    {
        using var gpu = new Fixture();
        RequireMesh(gpu);
        Target target = gpu.Texture();
        byte[] mesh = (amplification ? PayloadMesh : Mesh).Value.ToArray();
        byte[]? amplifying = amplification ? Amplification.Value.ToArray() : null;
        byte[] pixel = PixelShader.Value.ToArray();
        NativeGpuColorTargetDescription[] targets = [new(GpuFormat.Rgba8Unorm)];
        NativeGpuRasterPipelineHandle pipeline = gpu.MeshPipeline(mesh, pixel, amplifying,
            new() { Topology = null, MeshOutputTopology = NativeGpuMeshOutputTopology.Triangle, ColorTargets = targets });
        Array.Clear(mesh); Array.Clear(pixel);
        if (amplifying is not null) { Array.Clear(amplifying); }
        targets[0] = new(GpuFormat.D32Float);
        using NativeGpuCommandBuffer unused = gpu.Commands();
        unused.SetPipeline(pipeline);
        gpu.Submit(unused);
        Assert.Equal(0, gpu.Backend.RasterPipelineVariantCount(pipeline));
        using NativeGpuCommandBuffer commands = gpu.Commands();
        commands.DiscardTexture(target.View, GpuTextureLayout.ColorAttachment);
        commands.BeginRendering([new(target.RenderView, NativeGpuLoadOp.Clear)]);
        commands.SetPipeline(pipeline);
        byte[] root = Root(1, 1, 1);
        commands.DispatchMesh(root, 1);
        Array.Clear(root);
        commands.EndRendering();
        NativeGpuLinearRegion result = gpu.Readback(commands, target);

        gpu.Submit(commands);

        Assert.Equal(new byte[] { 255, 255, 255, 255 }, Pixel(result, 8, 8));
        Assert.Equal(1, gpu.Backend.RasterPipelineVariantCount(pipeline));
    }
}
