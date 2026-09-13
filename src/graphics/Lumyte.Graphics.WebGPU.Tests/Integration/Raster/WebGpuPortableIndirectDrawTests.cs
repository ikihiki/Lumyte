using System.Runtime.InteropServices;
using P = Lumyte.Graphics.Portable;
using Fixture = Lumyte.Graphics.WebGPU.Tests.WebGpuPortableRasterFixture;

namespace Lumyte.Graphics.WebGPU.Tests;

[Collection("GpuBackend")]
[Trait("Category", "WebGpuPortableConformance")]
public sealed class WebGpuPortableIndirectDrawTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ComputeProducedArgumentsDriveTheFollowingRasterPass(bool indexed)
    {
        using Fixture fixture = await Fixture.CreateAsync();
        P.GpuBufferHandle arguments = fixture.Buffer(64, P.GpuBufferUsage.Storage | P.GpuBufferUsage.IndirectArguments);
        P.GpuBindingLayoutHandle layout = fixture.Layout(new P.GpuBindingLayoutEntry(0, P.GpuShaderStage.Compute,
            new P.GpuBufferBindingLayout(P.GpuBufferBindingType.Storage)));
        P.GpuBindingsHandle bindings = fixture.Bindings(layout, P.GpuBindingEntry.Buffer(0, new(arguments)));
        string writer = indexed ? """
            @group(0) @binding(0) var<storage, read_write> output: array<u32>;
            @compute @workgroup_size(1) fn main() {
                output[4] = 3u; output[5] = 1u; output[6] = 1u; output[7] = 1u; output[8] = 0u;
            }
            """ : """
            @group(0) @binding(0) var<storage, read_write> output: array<u32>;
            @compute @workgroup_size(1) fn main() {
                output[4] = 3u; output[5] = 1u; output[6] = 0u; output[7] = 0u;
            }
            """;
        P.GpuComputePipelineHandle producer = fixture.ComputePipeline(writer, layout);
        string raster = indexed ? Fixture.SolidShader.Replace("positions[id]",
            "positions[select(0u, id - 1u, id >= 1u && id <= 3u)]", StringComparison.Ordinal)
            : Fixture.SolidShader;
        P.GpuRasterPipelineHandle pipeline = fixture.Pipeline(raster);
        P.GpuTextureHandle target = fixture.Texture();
        P.GpuCommandBuffer commands = fixture.Record();
        P.GpuBufferHandle? indices = null;
        if (indexed)
        {
            P.GpuBufferHandle upload = await fixture.UploadAsync(MemoryMarshal.AsBytes(new uint[] { 9, 0, 1, 2 }.AsSpan()).ToArray());
            indices = fixture.Buffer(16, P.GpuBufferUsage.Index | P.GpuBufferUsage.CopyDestination);
            commands.CopyBuffer(new(upload), new(indices));
        }
        commands.BeginCompute();
        commands.SetComputePipeline(producer);
        commands.SetComputeBindings(0, bindings);
        commands.Dispatch(1);
        commands.EndCompute();
        commands.BeginRendering([Fixture.Color(target)]);
        commands.SetPipeline(pipeline);

        if (indexed) { commands.DrawIndexedIndirect(new(indices!), P.GpuIndexFormat.Uint32, new(arguments, 16, 20)); }
        else { commands.DrawIndirect(new(arguments, 16, 16)); }
        commands.EndRendering();
        P.GpuBufferHandle readback = fixture.Readback(commands, target);
        await fixture.SubmitAndWaitAsync(commands);

        Assert.Equal(Fixture.Pixel.RedColor, await fixture.PixelAsync(readback));
    }
}
