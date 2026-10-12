using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Wgpu;
using Xunit;

namespace Lumyte.Graphics.Hosting.Tests;

/// <summary>Tests releasing native pass encoders when a lost command buffer is retired.</summary>
public sealed class NativePassRetirementTests
{
    /// <summary>Retirement releases unfinished render and compute passes without ending them on the lost device.</summary>
    /// <returns>Completion of native retirement confirmation and child cleanup.</returns>
    [NativeGpuFact]
    public async Task LossAllowsDisposalOfUnfinishedPassesAsync()
    {
        using var device = WgpuDevice.Create();
        using IGraphicsTexture texture = device.CreateTexture(new() { Width = 1, Height = 1, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.RenderAttachment });
        using IGraphicsTextureView view = texture.CreateView();
        using IGraphicsCommandBuffer renderCommands = device.CreateCommandBuffer(new());
        IRenderEncoder render = renderCommands.BeginRenderPass(new()
        {
            ColorAttachments = [new() { View = view, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store }],
        });
        using IGraphicsCommandBuffer computeCommands = device.CreateCommandBuffer(new());
        IComputeEncoder compute = computeCommands.BeginComputePass(new());
        Assert.Throws<InvalidOperationException>(() => renderCommands.Dispose());
        Assert.Throws<InvalidOperationException>(() => computeCommands.Dispose());

        NativeDeviceLossTests.Destroy(device);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (device.Status == GraphicsDeviceStatus.Ready)
        {
            timeout.Token.ThrowIfCancellationRequested();
            device.ProcessEvents();
            await Task.Delay(1, timeout.Token);
        }

        Assert.Throws<InvalidOperationException>(() => renderCommands.Dispose());
        Assert.Throws<InvalidOperationException>(() => computeCommands.Dispose());
        await device.WaitForRetirementAsync(timeout.Token);
        renderCommands.Dispose();
        computeCommands.Dispose();
        Assert.Equal(CommandBufferState.Disposed, renderCommands.State);
        Assert.Equal(CommandBufferState.Disposed, computeCommands.State);
        Assert.Throws<InvalidOperationException>(() => render.End());
        Assert.Throws<InvalidOperationException>(() => compute.End());
        renderCommands.Dispose();
        computeCommands.Dispose();
    }
}
