using System.Runtime.InteropServices;
using P = Lumyte.Graphics.Portable;
using Fixture = Lumyte.Graphics.WebGPU.Tests.WebGpuPortableRasterFixture;

namespace Lumyte.Graphics.WebGPU.Tests;

[Collection("GpuBackend")]
[Trait("Category", "WebGpuPortableConformance")]
public sealed class WebGpuPortableRasterBindingTests
{
    [Fact]
    public async Task FragmentShaderSamplesAnExplicitTextureAndSamplerBinding()
    {
        using Fixture fixture = await Fixture.CreateAsync();
        P.GpuTextureHandle sampled = fixture.Texture(1, 1,
            usage: P.GpuTextureUsage.Sampled | P.GpuTextureUsage.CopyDestination);
        P.GpuTextureHandle target = fixture.Texture();
        P.GpuBufferHandle upload = await fixture.UploadAsync([0, 255, 0, 255]);
        P.GpuBindingLayoutHandle layout = fixture.Layout(
            new(2, P.GpuShaderStage.Pixel, new P.GpuTextureBindingLayout(P.GpuTextureSampleType.Float)),
            new(5, P.GpuShaderStage.Pixel, new P.GpuSamplerBindingLayout(P.GpuSamplerBindingType.Filtering)));
        P.GpuBindingsHandle bindings = fixture.Bindings(layout,
            P.GpuBindingEntry.Texture(2, new(sampled)),
            P.GpuBindingEntry.Sampler(5, new P.GpuSamplerDescription()));
        string source = Fixture.SolidShader[..Fixture.SolidShader.IndexOf("@fragment", StringComparison.Ordinal)] + """
            @group(0) @binding(2) var image: texture_2d<f32>;
            @group(0) @binding(5) var imageSampler: sampler;
            @fragment fn fragment() -> @location(0) vec4f {
                return textureSample(image, imageSampler, vec2f(0.5));
            }
            """;
        P.GpuRasterPipelineHandle pipeline = fixture.Pipeline(source, bindingLayouts: [layout]);
        P.GpuCommandBuffer commands = fixture.Record();
        commands.CopyBufferToTexture(new(upload), sampled, new(0, P.GpuTextureAspect.All, default, new(1, 1, 1)));
        commands.BeginRendering([Fixture.Color(target)]);
        commands.SetPipeline(pipeline);
        commands.SetBindings(0, bindings);

        commands.Draw(3);
        commands.EndRendering();
        P.GpuBufferHandle readback = fixture.Readback(commands, target);
        await fixture.SubmitAndWaitAsync(commands);

        Assert.Equal(Fixture.Pixel.GreenColor, await fixture.PixelAsync(readback));
        Assert.Equal(1, fixture.Backend.CacheStatistics.ViewEntries);
    }

    [Fact]
    public async Task VertexPullingAndCopiedDynamicOffsetsSupplyRasterInputs()
    {
        using Fixture fixture = await Fixture.CreateAsync();
        uint alignment = fixture.Backend.Limits.MinUniformBufferOffsetAlignment;
        byte[] colors = new byte[alignment + 16];
        MemoryMarshal.AsBytes(new float[] { 0, 1, 0, 1 }.AsSpan()).CopyTo(colors.AsSpan((int)alignment));
        P.GpuBufferHandle colorUpload = await fixture.UploadAsync(colors);
        P.GpuBufferHandle uniforms = fixture.Buffer((ulong)colors.Length, P.GpuBufferUsage.Uniform | P.GpuBufferUsage.CopyDestination);
        byte[] vertices = MemoryMarshal.AsBytes(new float[] { -1, -1, 3, -1, -1, 3 }.AsSpan()).ToArray();
        P.GpuBufferHandle vertexUpload = await fixture.UploadAsync(vertices);
        P.GpuBufferHandle storage = fixture.Buffer((ulong)vertices.Length, P.GpuBufferUsage.Storage | P.GpuBufferUsage.CopyDestination);
        P.GpuBindingLayoutHandle layout = fixture.Layout(
            new(4, P.GpuShaderStage.Vertex, new P.GpuBufferBindingLayout(P.GpuBufferBindingType.ReadOnlyStorage)),
            new(1, P.GpuShaderStage.Pixel, new P.GpuBufferBindingLayout(P.GpuBufferBindingType.Uniform, 16, true)));
        P.GpuBindingsHandle bindings = fixture.Bindings(layout,
            P.GpuBindingEntry.Buffer(4, new(storage)), P.GpuBindingEntry.Buffer(1, new(uniforms, 0, 16)));
        const string source = """
            @group(0) @binding(4) var<storage, read> positions: array<vec2f>;
            @group(0) @binding(1) var<uniform> color: vec4f;
            @vertex fn vertex(@builtin(vertex_index) id: u32) -> @builtin(position) vec4f {
                return vec4f(positions[id], 0, 1);
            }
            @fragment fn fragment() -> @location(0) vec4f { return color; }
            """;
        P.GpuRasterPipelineHandle pipeline = fixture.Pipeline(source, bindingLayouts: [layout]);
        P.GpuTextureHandle target = fixture.Texture();
        uint[] offsets = [alignment];
        P.GpuCommandBuffer commands = fixture.Record();
        commands.CopyBuffer(new(colorUpload), new(uniforms));
        commands.CopyBuffer(new(vertexUpload), new(storage));
        commands.BeginRendering([Fixture.Color(target)]);
        commands.SetPipeline(pipeline);

        commands.SetBindings(0, bindings, offsets);
        Array.Clear(offsets);
        commands.Draw(3);
        commands.EndRendering();
        P.GpuBufferHandle readback = fixture.Readback(commands, target);
        await fixture.SubmitAndWaitAsync(commands);

        Assert.Equal(Fixture.Pixel.GreenColor, await fixture.PixelAsync(readback));
    }
}
