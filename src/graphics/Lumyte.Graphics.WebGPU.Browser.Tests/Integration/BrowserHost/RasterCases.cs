using System.Buffers.Binary;
using System.Runtime.InteropServices;
using P = Lumyte.Graphics.Portable;

public static partial class BrowserCases
{
    private static async Task<object> IndexedRasterAsync()
    {
        using BrowserCaseFixture fixture = await BrowserCaseFixture.CreateAsync();
        P.GpuBufferHandle upload = await fixture.UploadAsync(MemoryMarshal.AsBytes(new uint[] { 99, 8, 2, 3, 4 }.AsSpan()).ToArray());
        P.GpuBufferHandle indices = fixture.Buffer(20, P.GpuBufferUsage.Index | P.GpuBufferUsage.CopyDestination);
        P.GpuTextureHandle target = fixture.Texture();
        P.GpuTextureHandle copied = fixture.Texture();
        P.GpuBufferHandle readback = fixture.Buffer(4, P.GpuBufferUsage.MapRead | P.GpuBufferUsage.CopyDestination);
        P.GpuRasterPipelineHandle pipeline = fixture.Raster("""
            requires immediate_address_space;
            struct Root { color: u32, depth: f32 }
            var<immediate> root: Root;
            @vertex fn vertex(@builtin(vertex_index) id: u32) -> @builtin(position) vec4f {
                let positions = array<vec2f, 3>(vec2f(-1, -1), vec2f(3, -1), vec2f(-1, 3));
                return vec4f(positions[id], root.depth, 1);
            }
            @fragment fn fragment() -> @location(0) vec4f { return unpack4x8unorm(root.color); }
            """, 8);
        P.GpuCommandBuffer commands = fixture.Record();
        commands.CopyBuffer(new(upload), new(indices));
        commands.BeginRendering([new(new(target), P.GpuAttachmentLoadOperation.Clear, ClearColor: new(0, 0, 1, 1))]);
        commands.SetPipeline(pipeline);
        commands.SetViewportAndScissor(new(0, 0, 1, 1, 0, 1), new(0, 0, 1, 1));
        byte[] root = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(root, 0xff0000ff);
        commands.SetRootData(root);
        commands.DrawIndexed(new(indices, 4, 16), P.GpuIndexFormat.Uint32, 3, firstIndex: 1, baseVertex: -2);
        Array.Fill(root, (byte)99);
        commands.EndRendering();
        P.GpuTextureCopyFootprint footprint = new(0, P.GpuTextureAspect.All, new(0, 0, 0), new(1, 1, 1));
        commands.CopyTexture(target, footprint, copied, footprint);
        commands.CopyTextureToBuffer(copied, footprint, new(readback));
        await fixture.SubmitAsync(commands);
        return new { color = (await fixture.ReadAsync(readback, 4)).Select(item => (int)item).ToArray() };
    }

    private static async Task<object> TextureUploadAsync()
    {
        using BrowserCaseFixture fixture = await BrowserCaseFixture.CreateAsync();
        byte[] bytes = new byte[264];
        byte[] first = [255, 0, 0, 255, 0, 255, 0, 255];
        byte[] second = [0, 0, 255, 255, 255, 255, 255, 255];
        first.CopyTo(bytes, 0);
        second.CopyTo(bytes, 256);
        P.GpuBufferHandle upload = await fixture.UploadAsync(bytes);
        P.GpuBufferHandle readback = fixture.Buffer(264, P.GpuBufferUsage.MapRead | P.GpuBufferUsage.CopyDestination);
        P.GpuTextureHandle texture = fixture.Texture(2, 2);
        P.GpuTextureCopyFootprint footprint = new(0, P.GpuTextureAspect.All, new(0, 0, 0), new(2, 2, 1), RowPitch: 256);
        P.GpuCommandBuffer commands = fixture.Record();
        commands.CopyBufferToTexture(new(upload), texture, footprint);
        commands.CopyTextureToBuffer(texture, footprint, new(readback));
        await fixture.SubmitAsync(commands);
        byte[] actual = await fixture.ReadAsync(readback, 264);
        return new { pixels = actual.Take(8).Concat(actual.Skip(256).Take(8)).Select(item => (int)item).ToArray() };
    }
}
