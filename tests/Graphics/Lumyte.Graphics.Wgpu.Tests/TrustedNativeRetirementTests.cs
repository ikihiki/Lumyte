using System.Reflection;
using Ahjo.Wgpu.Native;
using Lumyte.Graphics.Abstractions;
using Xunit;
using A = Ahjo.Wgpu;

namespace Lumyte.Graphics.Wgpu.Tests;

/// <summary>Checks proof issuance after an actual native WebGPU loss callback.</summary>
public sealed class TrustedNativeRetirementTests
{
    /// <summary>Native destruction and callback draining certify the fixed device generation.</summary>
    /// <returns>Completion of native retirement confirmation.</returns>
    [Trait("Execution", "Gpu")]
    [Fact]
    public async Task NativeLossIssuesProofAsync()
    {
        if (Environment.GetEnvironmentVariable("LUMYTE_GRAPHICS_GPU_TESTS") != "1")
        {
            return;
        }

        using var device = WgpuDevice.Create();
        await Assert.ThrowsAsync<InvalidOperationException>(() => device.RetirementAuthority.WaitForRetirementAsync().AsTask());
        DestroyNative(device);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (device.Status == GraphicsDeviceStatus.Ready)
        {
            timeout.Token.ThrowIfCancellationRequested();
            device.ProcessEvents();
            await Task.Delay(1, timeout.Token);
        }

        GraphicsDeviceRetirementProof proof = await device.RetirementAuthority.WaitForRetirementAsync(timeout.Token);
        Assert.Same(device.RetirementAuthority.Generation, proof.Generation);
        Assert.Equal(GraphicsDeviceStatus.Lost, device.Status);
    }

    private static unsafe void DestroyNative(WgpuDevice device)
    {
        var native = (A.Device)typeof(WgpuDevice).GetProperty("NativeDevice", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(device)!;
        WGPU.wgpuDeviceDestroy(native.Handle);

        // Force delivery of the destroyed-device native error before event pumping.
        var descriptor = new WGPUCommandEncoderDescriptor { label = new() { length = nuint.MaxValue } };
        WGPUCommandEncoderImpl* encoder = WGPU.wgpuDeviceCreateCommandEncoder(native.Handle, &descriptor);
        if (encoder != null)
        {
            WGPU.wgpuCommandEncoderRelease(encoder);
        }
    }
}
