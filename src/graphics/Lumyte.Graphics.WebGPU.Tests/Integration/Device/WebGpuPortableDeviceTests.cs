using P = Lumyte.Graphics.Portable;

namespace Lumyte.Graphics.WebGPU.Tests;

[Collection("GpuBackend")]
[Trait("Category", "WebGpuPortableConformance")]
public sealed class WebGpuPortableDeviceTests
{
    [Fact]
    public async Task DeviceEnablesDirectRootInputWithoutUnrequestedOptionalFeatures()
    {
        using WebGpuBackend backend = await WebGpuBackend.CreateAsync();

        Assert.Equal(new P.GpuBackendCapabilities(DirectRootData: true), backend.Capabilities);
        Assert.True(backend.Limits.MaxImmediateSize > 0);
    }

    [Theory]
    [InlineData(8u)]
    [InlineData(16u)]
    public async Task EffectiveImmediateLimitSatisfiesTheRequestedCapacity(uint bytes)
    {
        using WebGpuBackend backend = await WebGpuBackend.CreateAsync(new()
        {
            RequiredLimits = new() { MaxImmediateSize = bytes },
        });

        Assert.True(backend.Limits.MaxImmediateSize >= bytes,
            $"Effective immediate capacity {backend.Limits.MaxImmediateSize} must satisfy the requested {bytes} bytes.");
        Assert.True(backend.Capabilities.DirectRootData);
    }

    [Fact]
    [Trait("RequiresFeatures", "DualSourceBlend,IndirectFirstInstance")]
    public async Task RequestedOptionalFeaturesAreEnabledOnTheCreatedDevice()
    {
        using WebGpuBackend backend = await WebGpuBackend.CreateAsync(new()
        {
            RequireDualSourceBlend = true,
            RequireIndirectFirstInstance = true,
        });

        Assert.Equal(new P.GpuBackendCapabilities(true, true, true), backend.Capabilities);
    }

    [Fact]
    public async Task ExcessiveRequiredLimitReportsTheRuntimeDiagnostic()
    {
        P.GpuOperationException failure = await Assert.ThrowsAsync<P.GpuOperationException>(async () =>
        {
            using WebGpuBackend backend = await WebGpuBackend.CreateAsync(new()
            {
                RequiredLimits = new() { MaxBufferSize = ulong.MaxValue - 1 },
            });
        });

        Assert.NotEmpty(failure.Diagnostics);
        Assert.All(failure.Diagnostics, diagnostic => Assert.False(string.IsNullOrWhiteSpace(diagnostic.Message)));
    }

    [Fact]
    public async Task DisposedDeviceRejectsResourceCreation()
    {
        WebGpuBackend backend = await WebGpuBackend.CreateAsync();
        backend.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
            backend.CreateBuffer(new(32, P.GpuBufferUsage.MapWrite | P.GpuBufferUsage.CopySource)));
        backend.Dispose();
    }
}
