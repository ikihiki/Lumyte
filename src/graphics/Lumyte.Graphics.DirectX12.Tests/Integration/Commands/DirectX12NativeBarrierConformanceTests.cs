using Lumyte.Graphics.Native;

namespace Lumyte.Graphics.DirectX12.Tests;

[Collection("GpuBackend")]
[Trait("Category", "DirectX12Conformance")]
public sealed class DirectX12NativeBarrierConformanceTests
{
    [Theory]
    [InlineData(GpuStage.Host, GpuAccess.HostWrite, GpuStage.Copy, GpuAccess.CopyRead)]
    [InlineData(GpuStage.Copy, GpuAccess.CopyWrite, GpuStage.All, GpuAccess.ShaderRead)]
    [InlineData(GpuStage.All, GpuAccess.ShaderRead | GpuAccess.ShaderWrite | GpuAccess.CopyRead | GpuAccess.CopyWrite,
        GpuStage.Copy, GpuAccess.CopyWrite)]
    [InlineData(GpuStage.Copy, GpuAccess.CopyWrite, GpuStage.Host, GpuAccess.HostRead)]
    public async Task TransferScopesSubmitAndComplete(GpuStage beforeStage, GpuAccess beforeAccess,
        GpuStage afterStage, GpuAccess afterAccess)
    {
        using DirectX12Backend backend = DirectX12Backend.Create();
        using NativeGpuCommandBuffer commands = backend.MainQueue.StartCommandRecording();
        using NativeGpuSemaphore semaphore = backend.CreateSemaphore();
        commands.Barrier(beforeStage, beforeAccess, afterStage, afterAccess);

        backend.MainQueue.Submit([commands], new(semaphore, 1));
        await semaphore.WaitAsync(1);

        Assert.True(semaphore.IsComplete(1));
    }
}
