using P = Lumyte.Graphics.Portable;

namespace Lumyte.Graphics.WebGPU.Tests;

[Collection("GpuBackend")]
[Trait("Category", "WebGpuPortableConformance")]
public sealed class WebGpuPortableCopyTests
{
    [Fact]
    public async Task OmittedCopyLengthsUseEachBuffersRemainingBytes()
    {
        using WebGpuPortableComputeFixture fixture = await WebGpuPortableComputeFixture.CreateAsync();
        uint[] values = Enumerable.Range(10, 10).Select(static value => (uint)value).ToArray();
        P.GpuBufferHandle upload = await fixture.UploadAsync(values);
        P.GpuBufferHandle readback = fixture.Buffer(48, P.GpuBufferUsage.MapRead | P.GpuBufferUsage.CopyDestination);
        P.GpuCommandBuffer commands = fixture.Record();

        commands.CopyBuffer(new(upload, 8), new(readback, 16));
        await fixture.SubmitAndWaitAsync(commands);

        Assert.Equal(new uint[] { 0, 0, 0, 0 }.Concat(values.Skip(2)), await fixture.ReadAsync(readback, 12));
    }

    [Fact]
    public async Task SubmittedRecordingArrayAndDisposedRecordingDoNotChangeAcceptedCopies()
    {
        using WebGpuPortableComputeFixture fixture = await WebGpuPortableComputeFixture.CreateAsync();
        uint[] values = Enumerable.Range(10, 16).Select(static value => (uint)value).ToArray();
        P.GpuBufferHandle upload = await fixture.UploadAsync(values);
        P.GpuBufferHandle gpu = fixture.Buffer(96, P.GpuBufferUsage.CopySource | P.GpuBufferUsage.CopyDestination);
        P.GpuBufferHandle readback = fixture.Buffer(64, P.GpuBufferUsage.MapRead | P.GpuBufferUsage.CopyDestination);
        P.GpuCommandBuffer first = fixture.Record();
        P.GpuCommandBuffer second = fixture.Record();
        first.CopyBuffer(new(upload, 16, 32), new(gpu, 32, 32));
        second.CopyBuffer(new(gpu, 32, 32), new(readback, 16, 32));
        P.GpuCommandBuffer[] commands = [first, second];
        P.GpuSemaphore completion = fixture.Semaphore();

        fixture.Queue.Submit(commands, completion, 1);
        Array.Clear(commands);
        first.Dispose();
        second.Dispose();
        await fixture.Queue.WaitAsync(completion, 1);

        Assert.Equal(new uint[] { 0, 0, 0, 0 }.Concat(values.Skip(4).Take(8)).Concat(new uint[4]),
            await fixture.ReadAsync(readback, 16));
    }
}
