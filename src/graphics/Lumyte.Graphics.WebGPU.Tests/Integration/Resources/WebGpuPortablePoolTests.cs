using Lumyte.Graphics.Portable.Resources;
using P = Lumyte.Graphics.Portable;

namespace Lumyte.Graphics.WebGPU.Tests;

[Collection("GpuBackend")]
[Trait("Category", "WebGpuPortableConformance")]
public sealed class WebGpuPortablePoolTests
{
    [Fact]
    public async Task BufferLeaseReusePreservesCompletedGpuWrites()
    {
        using WebGpuPortableComputeFixture fixture = await WebGpuPortableComputeFixture.CreateAsync();
        using var pool = new GpuBufferPool(fixture.Backend);
        uint[] expected = [17, 31, 47, 83];
        P.GpuBufferHandle upload = await fixture.UploadAsync(expected);
        P.GpuBufferHandle readback = fixture.Buffer(16, P.GpuBufferUsage.MapRead | P.GpuBufferUsage.CopyDestination);
        var description = new P.GpuBufferDescription(16, P.GpuBufferUsage.CopySource | P.GpuBufferUsage.CopyDestination);
        GpuBufferLease first = pool.Acquire(description);
        P.GpuBufferHandle originalHandle = first.Handle;
        using (P.GpuCommandBuffer commands = fixture.Record())
        {
            commands.CopyBuffer(new(upload), new(first.Handle));
            await fixture.SubmitAndWaitAsync(commands);
        }
        pool.Release(first);

        GpuBufferLease reused = pool.Acquire(description);
        P.GpuBufferHandle reusedHandle = reused.Handle;
        uint[] actual;
        using (P.GpuCommandBuffer commands = fixture.Record())
        {
            commands.CopyBuffer(new(reused.Handle), new(readback));
            await fixture.SubmitAndWaitAsync(commands);
            actual = await fixture.ReadAsync(readback, expected.Length);
        }
        pool.Release(reused);

        Assert.Same(originalHandle, reusedHandle);
        Assert.Equal(expected, actual);
        pool.Trim();
    }

    [Fact]
    public async Task TextureLeaseReusePreservesCompletedGpuWrites()
    {
        using WebGpuPortableComputeFixture fixture = await WebGpuPortableComputeFixture.CreateAsync();
        using var pool = new GpuTexturePool(fixture.Backend);
        uint[] expected = [0xFF113355, 0x88224466, 0xCC557799, 0xEE99BBDD];
        P.GpuBufferHandle upload = await fixture.UploadAsync(expected);
        P.GpuBufferHandle readback = fixture.Buffer(16, P.GpuBufferUsage.MapRead | P.GpuBufferUsage.CopyDestination);
        var description = new P.GpuTextureDescription(P.GpuTextureDimension.Texture2D, 4, 1, 1, 1, 1, 1,
            GpuFormat.Rgba8Unorm, P.GpuTextureUsage.CopySource | P.GpuTextureUsage.CopyDestination);
        var footprint = new P.GpuTextureCopyFootprint(0, P.GpuTextureAspect.All, default, new(4, 1, 1));
        GpuTextureLease first = pool.Acquire(description);
        P.GpuTextureHandle originalHandle = first.Handle;
        using (P.GpuCommandBuffer commands = fixture.Record())
        {
            commands.CopyBufferToTexture(new(upload), first.Handle, footprint);
            await fixture.SubmitAndWaitAsync(commands);
        }
        pool.Release(first);

        GpuTextureLease reused = pool.Acquire(description);
        P.GpuTextureHandle reusedHandle = reused.Handle;
        uint[] actual;
        using (P.GpuCommandBuffer commands = fixture.Record())
        {
            commands.CopyTextureToBuffer(reused.Handle, footprint, new(readback));
            await fixture.SubmitAndWaitAsync(commands);
            actual = await fixture.ReadAsync(readback, expected.Length);
        }
        pool.Release(reused);

        Assert.Same(originalHandle, reusedHandle);
        Assert.Equal(expected, actual);
        pool.Trim();
    }
}
