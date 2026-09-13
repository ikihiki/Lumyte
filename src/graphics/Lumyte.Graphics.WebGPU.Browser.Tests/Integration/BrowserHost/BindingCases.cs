using System.Buffers.Binary;
using P = Lumyte.Graphics.Portable;

public static partial class BrowserCases
{
    private static async Task<object> DynamicBindingsAsync()
    {
        using BrowserCaseFixture fixture = await BrowserCaseFixture.CreateAsync();
        uint alignment = fixture.Backend.Limits.MinUniformBufferOffsetAlignment;
        byte[] bytes = new byte[checked((int)alignment + 4)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan((int)alignment), 73);
        P.GpuBufferHandle upload = await fixture.UploadAsync(bytes);
        P.GpuBufferHandle uniform = fixture.Buffer((ulong)bytes.Length, P.GpuBufferUsage.Uniform | P.GpuBufferUsage.CopyDestination);
        P.GpuBufferHandle output = fixture.Buffer(4, P.GpuBufferUsage.Storage | P.GpuBufferUsage.CopySource);
        P.GpuBufferHandle readback = fixture.Buffer(4, P.GpuBufferUsage.MapRead | P.GpuBufferUsage.CopyDestination);
        P.GpuBindingLayoutHandle layout = fixture.Layout(
            new P.GpuBindingLayoutEntry(7, P.GpuShaderStage.Compute, new P.GpuBufferBindingLayout(P.GpuBufferBindingType.Uniform, HasDynamicOffset: true)),
            new P.GpuBindingLayoutEntry(0, P.GpuShaderStage.Compute, new P.GpuBufferBindingLayout(P.GpuBufferBindingType.Storage)));
        P.GpuBindingsHandle bindings = fixture.BindingEntries(layout,
            P.GpuBindingEntry.Buffer(7, new(uniform, 0, 4)), P.GpuBindingEntry.Buffer(0, new(output)));
        P.GpuComputePipelineHandle pipeline = fixture.Compute("""
            @group(0) @binding(7) var<uniform> value: u32;
            @group(0) @binding(0) var<storage, read_write> output: u32;
            @compute @workgroup_size(1) fn main() { output = value; }
            """, 0, "main", layout);
        P.GpuCommandBuffer commands = fixture.Record();
        commands.CopyBuffer(new(upload), new(uniform));
        commands.BeginCompute();
        commands.SetComputePipeline(pipeline);
        uint[] offsets = [alignment];
        commands.SetComputeBindings(0, bindings, offsets);
        offsets[0] = 0;
        commands.Dispatch(1);
        commands.EndCompute();
        commands.CopyBuffer(new(output), new(readback));
        await fixture.SubmitAsync(commands);
        return new { actual = BinaryPrimitives.ReadUInt32LittleEndian(await fixture.ReadAsync(readback, 4)) };
    }

    private static async Task<object> SampledTextureAsync()
    {
        using BrowserCaseFixture fixture = await BrowserCaseFixture.CreateAsync();
        P.GpuTextureHandle source = fixture.Texture();
        P.GpuTextureHandle target = fixture.Texture();
        P.GpuBufferHandle upload = await fixture.UploadAsync([64, 128, 192, 255]);
        P.GpuBufferHandle readback = fixture.Buffer(4, P.GpuBufferUsage.MapRead | P.GpuBufferUsage.CopyDestination);
        P.GpuBindingLayoutHandle layout = fixture.Layout(
            new P.GpuBindingLayoutEntry(0, P.GpuShaderStage.Pixel, new P.GpuTextureBindingLayout(P.GpuTextureSampleType.Float)),
            new P.GpuBindingLayoutEntry(1, P.GpuShaderStage.Pixel, new P.GpuSamplerBindingLayout(P.GpuSamplerBindingType.Filtering)));
        P.GpuBindingEntry[] entries = [P.GpuBindingEntry.Texture(0, new(source)), P.GpuBindingEntry.Sampler(1, new P.GpuSamplerDescription())];
        P.GpuBindingsHandle bindings = fixture.BindingEntries(layout, entries);
        Array.Clear(entries);
        P.GpuRasterPipelineHandle pipeline = fixture.Raster("""
            @group(0) @binding(0) var source: texture_2d<f32>;
            @group(0) @binding(1) var sourceSampler: sampler;
            @vertex fn vertex(@builtin(vertex_index) id: u32) -> @builtin(position) vec4f {
                let positions = array<vec2f, 3>(vec2f(-1, -1), vec2f(3, -1), vec2f(-1, 3));
                return vec4f(positions[id], 0, 1);
            }
            @fragment fn fragment() -> @location(0) vec4f { return textureSampleLevel(source, sourceSampler, vec2f(0.5), 0); }
            """, 0, layout);
        P.GpuTextureCopyFootprint footprint = new(0, P.GpuTextureAspect.All, new(0, 0, 0), new(1, 1, 1));
        P.GpuCommandBuffer commands = fixture.Record();
        commands.CopyBufferToTexture(new(upload), source, footprint);
        commands.BeginRendering([new(new(target), P.GpuAttachmentLoadOperation.Clear)]);
        commands.SetPipeline(pipeline);
        commands.SetBindings(0, bindings);
        commands.Draw(3);
        commands.EndRendering();
        commands.CopyTextureToBuffer(target, footprint, new(readback));
        await fixture.SubmitAsync(commands);
        return new { color = (await fixture.ReadAsync(readback, 4)).Select(item => (int)item).ToArray() };
    }
}
