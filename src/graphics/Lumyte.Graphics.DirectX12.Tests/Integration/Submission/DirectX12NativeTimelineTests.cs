using System.Runtime.InteropServices;
using Lumyte.Graphics.Native;
using Fixture = Lumyte.Graphics.DirectX12.Tests.DirectX12NativeRasterTests.Fixture;
using static Lumyte.Graphics.DirectX12.Tests.DirectX12NativeRasterTests;

namespace Lumyte.Graphics.DirectX12.Tests;

[Collection("GpuBackend")]
[Trait("Category", "DirectX12Conformance")]
public sealed partial class DirectX12NativeTimelineTests
{
    [Fact]
    public async Task CpuCanSubmitSeveralFramesWhileAnotherThreadWaitsForTheFirst()
    {
        using var gpu = new Fixture();
        NativeGpuQueue copyQueue = Assert.IsAssignableFrom<NativeGpuQueue>(gpu.Backend.CopyQueue);
        NativeGpuLinearRegion upload = gpu.Region(NativeGpuMemoryKind.CpuVisible);
        NativeGpuLinearRegion input = gpu.Region(NativeGpuMemoryKind.GpuOnly);
        NativeGpuLinearRegion output = gpu.Region(NativeGpuMemoryKind.GpuOnly);
        NativeGpuLinearRegion readback = gpu.Region(NativeGpuMemoryKind.Readback);
        for (int index = 0; index < 3; index++) { Marshal.WriteInt32(upload.CpuAddress + 256 + index * 4, 41 + index); }
        gpu.WriteBuffer(2, new(input, 512, 4), NativeGpuBufferAccess.ReadOnly);
        gpu.WriteBuffer(3, new(output, 512, 256), NativeGpuBufferAccess.ReadWrite);
        NativeGpuComputePipelineHandle pipeline = Compute(gpu, """
            cbuffer Root:register(b0) {uint inputIndex;uint outputIndex;uint frame;};
            [numthreads(1,1,1)] void computeMain() {
                ByteAddressBuffer input=ResourceDescriptorHeap[inputIndex];
                RWByteAddressBuffer output=ResourceDescriptorHeap[outputIndex];
                output.Store(frame*4,input.Load(0)+frame*10);
            }
            """);
        using NativeGpuSemaphore gate = gpu.Backend.CreateSemaphore();
        using NativeGpuSemaphore uploaded = gpu.Backend.CreateSemaphore();
        using NativeGpuSemaphore rendered = gpu.Backend.CreateSemaphore();
        Task? waiter = null;
        ulong copied = 0, submitted = 0;
        try
        {
            for (uint frame = 0; frame < 3; frame++)
            {
                using NativeGpuCommandBuffer transfer = copyQueue.StartCommandRecording();
                transfer.CopyMemory(new(upload, 256 + frame * 4, 4), new(input, 512, 4));
                copyQueue.Submit([transfer], new(uploaded, frame + 1),
                    frame == 0 ? [new(gate, 1)] : [new(rendered, frame)]);
                copied = frame + 1;
                using NativeGpuCommandBuffer work = gpu.Commands();
                work.SetComputePipeline(pipeline);
                work.Dispatch(MemoryMarshal.AsBytes(new uint[] { 2, 3, frame }.AsSpan()), 1);
                if (frame == 2)
                {
                    work.Barrier(GpuStage.ComputeShader, GpuAccess.ShaderWrite, GpuStage.Copy, GpuAccess.CopyRead);
                    work.CopyMemory(new(output, 512, 12), new(readback, 0, 12));
                    work.Barrier(GpuStage.Copy, GpuAccess.CopyWrite, GpuStage.Host, GpuAccess.HostRead);
                }
                gpu.Backend.MainQueue.Submit([work], new(rendered, frame + 1), [new(uploaded, frame + 1)]);
                submitted = frame + 1;
                if (frame == 0)
                {
                    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    waiter = Task.Run(() => { entered.SetResult(); rendered.WaitCpu(1); });
                    await entered.Task;
                }
            }

            // The CPU has submitted all three frames while both GPU queues and the
            // first-frame CPU waiter still depend on an explicitly closed host gate.
            Assert.False(uploaded.IsComplete(3));
            Assert.False(rendered.IsComplete(3));
        }
        finally
        {
            gate.SignalCpu(1);
            if (copied != 0) { uploaded.WaitCpu(copied); }
            if (submitted != 0) { rendered.WaitCpu(submitted); }
            if (waiter is not null) { await waiter; }
            gpu.Backend.DestroyComputePipeline(pipeline);
        }

        var actual = new int[3];
        Marshal.Copy(readback.CpuAddress, actual, 0, actual.Length);
        Assert.Equal(new int[] { 41, 52, 63 }, actual);
    }

    [Fact]
    public void TextureUploadUsesExplicitMainCopyMainTimelineHandoffs()
    {
        using var gpu = new Fixture();
        NativeGpuQueue copy = Assert.IsAssignableFrom<NativeGpuQueue>(gpu.Backend.CopyQueue);
        Target texture = gpu.Texture(additionalUsage: NativeGpuTextureUsage.Sampled);
        NativeGpuLinearRegion upload = gpu.Region(NativeGpuMemoryKind.CpuVisible);
        NativeGpuLinearRegion output = gpu.Region(NativeGpuMemoryKind.GpuOnly);
        NativeGpuLinearRegion readback = gpu.Region(NativeGpuMemoryKind.Readback);
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++) { Marshal.WriteInt32(upload.CpuAddress + 512 + y * 256 + x * 4, unchecked((int)0xFF003529)); }
        }
        gpu.WriteTexture(2, texture.View);
        gpu.WriteBuffer(3, new(output, 512, 16), NativeGpuBufferAccess.ReadWrite);
        NativeGpuComputePipelineHandle pipeline = Compute(gpu, """
            cbuffer Root:register(b0) {uint textureIndex;uint outputIndex;};
            [numthreads(1,1,1)] void computeMain() {
                Texture2D<float4> texture=ResourceDescriptorHeap[textureIndex];
                RWByteAddressBuffer output=ResourceDescriptorHeap[outputIndex];
                output.Store2(0,(uint2)round(texture.Load(int3(7,9,0)).rg*255));
            }
            """);
        using NativeGpuSemaphore ready = gpu.Backend.CreateSemaphore();
        using NativeGpuSemaphore uploaded = gpu.Backend.CreateSemaphore();
        using NativeGpuSemaphore consumed = gpu.Backend.CreateSemaphore();
        try
        {
            using NativeGpuCommandBuffer prepare = gpu.Backend.MainQueue.StartCommandRecording();
            prepare.DiscardTexture(texture.View, GpuTextureLayout.Common);
            gpu.Backend.MainQueue.Submit([prepare], new(ready, 1));
            using NativeGpuCommandBuffer transfer = copy.StartCommandRecording();
            transfer.CopyMemoryToTexture(new(upload, 512, 4096), texture.Texture,
                new(0, NativeGpuTextureAspect.Color, 0, 1, default, new(16, 16, 1), 256, 4096));
            copy.Submit([transfer], new(uploaded, 1), [new(ready, 1)]);
            using NativeGpuCommandBuffer use = gpu.Commands();
            use.TextureTransition(texture.View, GpuTextureLayout.Common, GpuTextureLayout.ShaderRead);
            use.SetComputePipeline(pipeline);
            use.Dispatch(MemoryMarshal.AsBytes(new uint[] { 2, 3 }.AsSpan()), 1);
            use.Barrier(GpuStage.ComputeShader, GpuAccess.ShaderWrite, GpuStage.Copy, GpuAccess.CopyRead);
            use.CopyMemory(new(output, 512, 8), new(readback, 0, 8));
            use.Barrier(GpuStage.Copy, GpuAccess.CopyWrite, GpuStage.Host, GpuAccess.HostRead);
            gpu.Backend.MainQueue.Submit([use], new(consumed, 1), [new(uploaded, 1)]);
            consumed.WaitCpu(1);

            var actual = new int[2];
            Marshal.Copy(readback.CpuAddress, actual, 0, actual.Length);
            Assert.Equal(new int[] { 41, 53 }, actual);
        }
        finally { gpu.Backend.DestroyComputePipeline(pipeline); }
    }

    [Fact]
    public void CopyQueueReadsATextureAfterMainRenderingReleasesItToCommon()
    {
        using var gpu = new Fixture();
        NativeGpuQueue copy = Assert.IsAssignableFrom<NativeGpuQueue>(gpu.Backend.CopyQueue);
        Target texture = gpu.Texture();
        NativeGpuLinearRegion readback = gpu.Region(NativeGpuMemoryKind.Readback);
        using NativeGpuSemaphore rendered = gpu.Backend.CreateSemaphore();
        using NativeGpuSemaphore copied = gpu.Backend.CreateSemaphore();
        using NativeGpuCommandBuffer render = gpu.Backend.MainQueue.StartCommandRecording();
        render.DiscardTexture(texture.View, GpuTextureLayout.ColorAttachment);
        render.BeginRendering([new(texture.RenderView, NativeGpuLoadOp.Clear, ClearColor: new(0, 1, 0, 1))]);
        render.EndRendering();
        render.TextureTransition(texture.View, GpuTextureLayout.ColorAttachment, GpuTextureLayout.Common);
        gpu.Backend.MainQueue.Submit([render], new(rendered, 1));
        using NativeGpuCommandBuffer transfer = copy.StartCommandRecording();
        transfer.CopyTextureToMemory(texture.Texture, new(readback, 512, 4096),
            new(0, NativeGpuTextureAspect.Color, 0, 1, default, new(16, 16, 1), 256, 4096));
        transfer.Barrier(GpuStage.Copy, GpuAccess.CopyWrite, GpuStage.Host, GpuAccess.HostRead);

        copy.Submit([transfer], new(copied, 1), [new(rendered, 1)]);
        copied.WaitCpu(1);

        var pixel = new byte[4];
        Marshal.Copy(readback.CpuAddress + 512 + 8 * 256 + 8 * 4, pixel, 0, pixel.Length);
        Assert.Equal(new byte[] { 0, 255, 0, 255 }, pixel);
    }

    private static NativeGpuComputePipelineHandle Compute(Fixture gpu, string source)
        => gpu.Backend.CreateComputePipeline(new(new NativeGpuShaderCode
            { Stage = GpuShaderStage.Compute, Code = DirectX12NativeComputeTests.Compile(source) }));
}
