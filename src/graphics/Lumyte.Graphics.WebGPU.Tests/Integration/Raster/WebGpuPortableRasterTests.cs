using System.Runtime.InteropServices;
using P = Lumyte.Graphics.Portable;
using Fixture = Lumyte.Graphics.WebGPU.Tests.WebGpuPortableRasterFixture;

namespace Lumyte.Graphics.WebGPU.Tests;

[Collection("GpuBackend")]
[Trait("Category", "WebGpuPortableConformance")]
public sealed class WebGpuPortableRasterTests
{
    [Fact]
    public async Task RenderingIntoOneDepthSliceLeavesTheOtherThreeDimensionalSliceUnchanged()
    {
        using Fixture fixture = await Fixture.CreateAsync();
        P.GpuTextureHandle target = fixture.Texture(dimension: P.GpuTextureDimension.Texture3D, depth: 2);
        P.GpuRasterPipelineHandle pipeline = fixture.Pipeline();
        P.GpuCommandBuffer commands = fixture.Record();
        commands.BeginRendering([Fixture.Color(target) with { DepthSlice = 1 }]);
        commands.SetPipeline(pipeline);

        commands.Draw(3);
        commands.EndRendering();
        P.GpuBufferHandle drawn = fixture.Readback(commands, target, layer: 1);
        P.GpuBufferHandle untouched = fixture.Readback(commands, target, layer: 0);
        await fixture.SubmitAndWaitAsync(commands);

        Assert.Equal(Fixture.Pixel.RedColor, await fixture.PixelAsync(drawn));
        Assert.Equal(Fixture.Pixel.Transparent, await fixture.PixelAsync(untouched));
    }

    [Fact]
    public async Task AttachmentAndRootInputsAreCopiedAndScissorLimitsDrawing()
    {
        using Fixture fixture = await Fixture.CreateAsync();
        P.GpuTextureHandle target = fixture.Texture();
        P.GpuRasterPipelineHandle pipeline = fixture.Pipeline(Fixture.RootShader, immediateSize: 8);
        P.GpuColorAttachment[] colors = [Fixture.Color(target, new(0, 0, 1, 1))];
        byte[] root = MemoryMarshal.AsBytes(new Fixture.Root[] { new(0xff0000ff) }.AsSpan()).ToArray();
        P.GpuCommandBuffer commands = fixture.Record();
        commands.BeginRendering(colors);
        commands.SetPipeline(pipeline);
        commands.SetViewportAndScissor(new(0, 0, 4, 4), new(0, 0, 2, 4));

        commands.SetRootData(root);
        Array.Clear(root);
        colors[0] = default;
        commands.Draw(3);
        commands.EndRendering();
        P.GpuBufferHandle readback = fixture.Readback(commands, target);
        await fixture.SubmitAndWaitAsync(commands);

        Assert.Equal(Fixture.Pixel.RedColor, await fixture.PixelAsync(readback, 0, 1));
        Assert.Equal(Fixture.Pixel.BlueColor, await fixture.PixelAsync(readback, 3, 1));
        Assert.Equal(0, fixture.Backend.CacheStatistics.ViewEntries);
    }

    [Fact]
    public async Task PipelineRebindingPreservesGenericRootInput()
    {
        using Fixture fixture = await Fixture.CreateAsync();
        P.GpuTextureHandle target = fixture.Texture();
        P.GpuRasterPipelineHandle pipeline = fixture.Pipeline(Fixture.RootShader, immediateSize: 8);
        P.GpuCommandBuffer commands = fixture.Record();
        commands.BeginRendering([Fixture.Color(target)]);
        commands.SetPipeline(pipeline);
        commands.SetRootData(new Fixture.Root(0xff00ff00));
        commands.SetViewportAndScissor(new(0, 0, 4, 4), new(0, 0, 2, 4));
        commands.Draw(3);

        commands.SetPipeline(pipeline);
        commands.SetViewportAndScissor(new(0, 0, 4, 4), new(2, 0, 2, 4));
        commands.Draw(3);
        commands.EndRendering();
        P.GpuBufferHandle readback = fixture.Readback(commands, target);
        await fixture.SubmitAndWaitAsync(commands);

        Assert.Equal(Fixture.Pixel.GreenColor, await fixture.PixelAsync(readback, 0, 1));
        Assert.Equal(Fixture.Pixel.GreenColor, await fixture.PixelAsync(readback, 3, 1));
    }

    [Theory]
    [InlineData(P.GpuIndexFormat.Uint16)]
    [InlineData(P.GpuIndexFormat.Uint32)]
    public async Task IndexedDrawAppliesRangeFirstIndexBaseVertexAndFirstInstance(P.GpuIndexFormat format)
    {
        using Fixture fixture = await Fixture.CreateAsync();
        byte[] data = format == P.GpuIndexFormat.Uint16
            ? MemoryMarshal.AsBytes(new ushort[] { 7, 8, 9, 0, 1, 2 }.AsSpan()).ToArray()
            : MemoryMarshal.AsBytes(new uint[] { 7, 8, 9, 0, 1, 2 }.AsSpan()).ToArray();
        P.GpuBufferHandle upload = await fixture.UploadAsync(data);
        P.GpuBufferHandle indices = fixture.Buffer((ulong)data.Length, P.GpuBufferUsage.Index | P.GpuBufferUsage.CopyDestination);
        uint elementSize = format == P.GpuIndexFormat.Uint16 ? 2u : 4u;
        P.GpuTextureHandle target = fixture.Texture();
        const string source = """
            struct VertexOutput { @builtin(position) position: vec4f, @location(0) @interpolate(flat) instance: u32 }
            @vertex fn vertex(@builtin(vertex_index) id: u32, @builtin(instance_index) instance: u32) -> VertexOutput {
                let positions = array<vec2f, 3>(vec2f(-1, -1), vec2f(3, -1), vec2f(-1, 3));
                // Wrong offsets collapse the triangle instead of relying on robust out-of-bounds reads.
                let index = select(0u, id - 1u, id >= 1u && id <= 3u);
                return VertexOutput(vec4f(positions[index], 0, 1), instance);
            }
            @fragment fn fragment(input: VertexOutput) -> @location(0) vec4f {
                return select(vec4f(0, 0, 1, 1), vec4f(1, 0, 0, 1), input.instance == 7u);
            }
            """;
        P.GpuRasterPipelineHandle pipeline = fixture.Pipeline(source);
        P.GpuCommandBuffer commands = fixture.Record();
        commands.CopyBuffer(new(upload), new(indices));
        commands.BeginRendering([Fixture.Color(target)]);
        commands.SetPipeline(pipeline);

        commands.DrawIndexed(new(indices, elementSize * 2, elementSize * 4), format,
            indexCount: 3, firstIndex: 1, baseVertex: 1, firstInstance: 7);
        commands.EndRendering();
        P.GpuBufferHandle readback = fixture.Readback(commands, target);
        await fixture.SubmitAndWaitAsync(commands);

        Assert.Equal(Fixture.Pixel.RedColor, await fixture.PixelAsync(readback));
    }

    [Fact]
    public async Task ASelectedMipAndArrayLayerCanBeUsedAsAnAttachment()
    {
        using Fixture fixture = await Fixture.CreateAsync();
        P.GpuTextureHandle target = fixture.Texture(8, 8, mipCount: 2, layers: 2);
        var selected = new P.GpuTextureView(target, new(Dimension: P.GpuTextureViewDimension.Texture2D,
            BaseMip: 1, MipCount: 1, BaseLayer: 1, LayerCount: 1));
        P.GpuCommandBuffer commands = fixture.Record();

        commands.BeginRendering([new(selected, P.GpuAttachmentLoadOperation.Clear, ClearColor: new(0, 1, 0, 1))]);
        commands.EndRendering();
        P.GpuBufferHandle readback = fixture.Readback(commands, target, mip: 1, layer: 1);
        await fixture.SubmitAndWaitAsync(commands);

        Assert.Equal(Fixture.Pixel.GreenColor, await fixture.PixelAsync(readback));
        Assert.Equal(0, fixture.Backend.RasterPipelineStatistics.Creations);
    }

    [Fact]
    public async Task FragmentOutputsReachTheirSeparateColorAttachments()
    {
        using Fixture fixture = await Fixture.CreateAsync();
        P.GpuTextureHandle first = fixture.Texture();
        P.GpuTextureHandle second = fixture.Texture();
        string source = Fixture.SolidShader[..Fixture.SolidShader.IndexOf("@fragment", StringComparison.Ordinal)] + """
            struct Outputs { @location(0) first: vec4f, @location(1) second: vec4f }
            @fragment fn fragment() -> Outputs { return Outputs(vec4f(1, 0, 0, 1), vec4f(0, 1, 0, 1)); }
            """;
        P.GpuRasterPipelineHandle pipeline = fixture.Pipeline(source,
            new P.GpuRasterPipelineDescription([new(GpuFormat.Rgba8Unorm), new(GpuFormat.Rgba8Unorm)]));
        P.GpuCommandBuffer commands = fixture.Record();
        commands.BeginRendering([Fixture.Color(first), Fixture.Color(second)]);
        commands.SetPipeline(pipeline);

        commands.Draw(3);
        commands.EndRendering();
        P.GpuBufferHandle firstReadback = fixture.Readback(commands, first);
        P.GpuBufferHandle secondReadback = fixture.Readback(commands, second);
        await fixture.SubmitAndWaitAsync(commands);

        Assert.Equal(Fixture.Pixel.RedColor, await fixture.PixelAsync(firstReadback));
        Assert.Equal(Fixture.Pixel.GreenColor, await fixture.PixelAsync(secondReadback));
    }

    [Fact]
    public async Task MultisampledDrawingResolvesIntoTheSpecifiedTexture()
    {
        using Fixture fixture = await Fixture.CreateAsync();
        P.GpuTextureHandle multisampled = fixture.Texture(samples: 4, usage: P.GpuTextureUsage.ColorAttachment);
        P.GpuTextureHandle resolved = fixture.Texture();
        P.GpuRasterPipelineHandle pipeline = fixture.Pipeline(description:
            new P.GpuRasterPipelineDescription([new(GpuFormat.Rgba8Unorm)]) { SampleCount = 4 });
        P.GpuCommandBuffer commands = fixture.Record();
        commands.BeginRendering([Fixture.Color(multisampled) with { ResolveTarget = new(resolved) }]);
        commands.SetPipeline(pipeline);

        commands.Draw(3);
        commands.EndRendering();
        P.GpuBufferHandle readback = fixture.Readback(commands, resolved);
        await fixture.SubmitAndWaitAsync(commands);

        Assert.Equal(Fixture.Pixel.RedColor, await fixture.PixelAsync(readback));
        Assert.Equal(0, fixture.Backend.CacheStatistics.ViewEntries);
    }
}
