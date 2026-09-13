using Lumyte.Graphics.Portable.Resources;
using P = Lumyte.Graphics.Portable;

public static partial class BrowserCases
{
    private static async Task<object> BufferPoolAsync()
    {
        using BrowserCaseFixture fixture = await BrowserCaseFixture.CreateAsync();
        using var pool = new GpuBufferPool(fixture.Backend);
        P.GpuBufferHandle upload = await fixture.UploadAsync([3, 5, 7, 11, 13, 17, 19, 23]);
        var description = new P.GpuBufferDescription(8, P.GpuBufferUsage.CopyDestination | P.GpuBufferUsage.MapRead);
        GpuBufferLease first = pool.Acquire(description);
        P.GpuBufferHandle original = first.Handle;
        using (P.GpuCommandBuffer commands = fixture.Record())
        {
            commands.CopyBuffer(new(upload), new(original));
            await fixture.SubmitAsync(commands);
        }
        pool.Release(first);
        GpuBufferLease second = pool.Acquire(description);
        byte[] actual = await fixture.ReadAsync(second.Handle, 8);
        bool reused = ReferenceEquals(original, second.Handle);
        pool.Release(second);
        return new { reused, actual = actual.Select(item => (int)item).ToArray() };
    }

    private static async Task<object> TexturePoolAsync()
    {
        using BrowserCaseFixture fixture = await BrowserCaseFixture.CreateAsync();
        using var pool = new GpuTexturePool(fixture.Backend);
        var description = new P.GpuTextureDescription(P.GpuTextureDimension.Texture2D, 1, 1, 1, 1, 1, 1,
            Lumyte.Graphics.GpuFormat.Rgba8Unorm, P.GpuTextureUsage.CopySource | P.GpuTextureUsage.CopyDestination);
        P.GpuBufferHandle upload = await fixture.UploadAsync([31, 63, 127, 255]);
        P.GpuBufferHandle readback = fixture.Buffer(4, P.GpuBufferUsage.MapRead | P.GpuBufferUsage.CopyDestination);
        var footprint = new P.GpuTextureCopyFootprint(0, P.GpuTextureAspect.All, new(0, 0, 0), new(1, 1, 1));
        GpuTextureLease first = pool.Acquire(description);
        P.GpuTextureHandle original = first.Handle;
        using (P.GpuCommandBuffer commands = fixture.Record())
        {
            commands.CopyBufferToTexture(new(upload), original, footprint);
            await fixture.SubmitAsync(commands);
        }
        pool.Release(first);
        GpuTextureLease second = pool.Acquire(description);
        using (P.GpuCommandBuffer commands = fixture.Record())
        {
            commands.CopyTextureToBuffer(second.Handle, footprint, new(readback));
            await fixture.SubmitAsync(commands);
        }
        byte[] actual = await fixture.ReadAsync(readback, 4);
        bool reused = ReferenceEquals(original, second.Handle);
        pool.Release(second);
        return new { reused, actual = actual.Select(item => (int)item).ToArray() };
    }
}
