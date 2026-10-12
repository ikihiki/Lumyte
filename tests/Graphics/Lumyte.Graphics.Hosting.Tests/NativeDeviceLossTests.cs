using System.Runtime.CompilerServices;
using Ahjo.Wgpu.Native;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Xunit;
using A = Ahjo.Wgpu;

namespace Lumyte.Graphics.Hosting.Tests;

/// <summary>Tests native WebGPU loss callbacks and Vulkan loss retirement against real devices.</summary>
public sealed class NativeDeviceLossTests
{
    /// <summary>Native WebGPU destruction delivers loss and fails submitted work without freeing callback storage early.</summary>
    /// <returns>Completion of native retirement confirmation.</returns>
    [NativeGpuFact]
    public async Task NativeWebGpuLossInvalidatesWorkAndConfirmsRetirementAsync()
    {
        using var device = WgpuDevice.Create();
        using IGraphicsBuffer<int> buffer = device.CreateBuffer(new BufferDesc<int> { Count = 4, Usage = BufferUsage.CopyDestination });
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        int notifications = 0;
        device.DeviceLost += (_, _) => notifications++;
        Destroy(device);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (device.Status == GraphicsDeviceStatus.Ready)
        {
            timeout.Token.ThrowIfCancellationRequested();
            device.ProcessEvents();
            await Task.Delay(1, timeout.Token);
        }

        Assert.Equal(GraphicsDeviceStatus.Lost, device.Status);
        Assert.Equal(1, notifications);
        Assert.Equal(SubmissionStatus.Failed, submission.Status);
        Assert.Equal(CommandBufferState.Faulted, commands.State);
        await Assert.ThrowsAsync<GraphicsDeviceLostException>(() => submission.WaitAsync().AsTask());
        await device.WaitForRetirementAsync(timeout.Token);
    }

    /// <summary>An injected fatal result uses real Vulkan idle confirmation and preserves explicit child disposal.</summary>
    /// <returns>Completion of native retirement confirmation.</returns>
    [NativeGpuFact]
    public async Task VulkanLossFailsPendingWorkAndRequiresRetirementAsync()
    {
        using var device = VulkanDevice.Create();
        using IGraphicsBuffer<int> buffer = device.CreateBuffer(new BufferDesc<int> { Count = 4, Usage = BufferUsage.CopyDestination });
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        int notifications = 0;
        device.DeviceLost += (_, _) => notifications++;
        Assert.Throws<GraphicsDeviceLostException>(() => CheckResult(device, Silk.NET.Vulkan.Result.ErrorDeviceLost, "Injected native result"));
        ReportLoss(device, "Repeated native result");
        Assert.Equal(1, notifications);
        Assert.Equal(SubmissionStatus.Failed, submission.Status);
        Assert.Equal(CommandBufferState.Faulted, commands.State);
        await Assert.ThrowsAsync<GraphicsDeviceLostException>(() => submission.WaitAsync().AsTask());
        await device.WaitForRetirementAsync();
    }

    /// <summary>Already released WebGPU resources remain idempotent while live resources still require retirement.</summary>
    /// <returns>Completion of native retirement confirmation.</returns>
    [NativeGpuFact]
    public async Task NativeWebGpuRepeatedDisposalAfterLossIsIdempotentAsync()
    {
        using var device = WgpuDevice.Create();
        await AssertRepeatedDisposalAsync(device, () => Destroy(device));
    }

    /// <summary>Already released Vulkan resources remain idempotent while live resources still require retirement.</summary>
    /// <returns>Completion of native retirement confirmation.</returns>
    [NativeGpuFact]
    public async Task VulkanRepeatedDisposalAfterLossIsIdempotentAsync()
    {
        using var device = VulkanDevice.Create();
        await AssertRepeatedDisposalAsync(device, () => ReportLoss(device, "Injected loss after resource disposal"));
    }

    internal static unsafe void Destroy(WgpuDevice device)
    {
        A.Device nativeDevice = GetNativeDevice(device);
        WGPU.wgpuDeviceDestroy(nativeDevice.Handle);

        // wgpu-native reports the destroyed-device error at the next native operation.
        var desc = new WGPUCommandEncoderDescriptor { label = new() { length = nuint.MaxValue } };
        WGPUCommandEncoderImpl* encoder = WGPU.wgpuDeviceCreateCommandEncoder(nativeDevice.Handle, &desc);
        if (encoder != null)
        {
            WGPU.wgpuCommandEncoderRelease(encoder);
        }
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_NativeDevice")]
    private static extern A.Device GetNativeDevice(WgpuDevice device);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "CheckResult")]
    private static extern void CheckResult(VulkanDevice device, Silk.NET.Vulkan.Result result, string operation);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "ReportLoss")]
    private static extern void ReportLoss(VulkanDevice device, string message);

    private static async Task AssertRepeatedDisposalAsync(IGraphicDevice device, Action lose)
    {
        IDisposable[] released =
        [
            device.CreateBuffer(new BufferDesc<int> { Count = 4, Usage = BufferUsage.CopyDestination }),
            device.CreateTexture(new() { Width = 1, Height = 1, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.CopyDestination }),
            device.CreateSampler(new()),
            device.CreateCommandBuffer(new()),
        ];
        foreach (IDisposable resource in released)
        {
            resource.Dispose();
        }

        using IGraphicsBuffer<int> live = device.CreateBuffer(new BufferDesc<int> { Count = 4, Usage = BufferUsage.CopyDestination });
        lose();
        bool retired = false;
        try
        {
            Assert.Equal(GraphicsDeviceStatus.Lost, device.Status);
            foreach (IDisposable resource in released)
            {
                resource.Dispose();
            }

            await device.WaitForRetirementAsync();
            retired = true;
            live.Dispose();
        }
        finally
        {
            if (!retired)
            {
                await device.WaitForRetirementAsync();
            }
        }
    }
}
