using Xunit.Abstractions;

namespace Lumyte.Graphics.Vulkan.Tests;

[Collection("GpuBackend")]
public sealed class VulkanBackendTests(ITestOutputHelper output)
{
    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void OptionalMeshCapabilitiesMatchExposedLimits()
    {
        using var backend = VulkanBackend.Create();
        output.WriteLine($"Capabilities: {backend.Capabilities}; Mesh limits: {backend.Limits.MeshShader}");

        Assert.Equal(backend.Capabilities.MeshShaders, backend.Limits.MeshShader.HasValue);
        Assert.Equal(backend.Capabilities.AmplificationShaders, backend.Limits.MeshShader?.AmplificationDispatch.HasValue ?? false);
    }

    [Fact]
    [Trait("Category", "VulkanNativeConformance")]
    public void FactoryCreatesANativeDeviceOrReportsMissingNativeRequirements()
    {
        VulkanBackend? backend = null;
        try
        {
            Exception? failure = Record.Exception(() => backend = VulkanBackend.Create());
            if (failure is null)
            {
                Assert.NotNull(backend);
                Assert.Equal(GpuShaderCodeFormat.SpirV, backend.ShaderCodeFormat);
                return;
            }

            var unsupported = Assert.IsType<NotSupportedException>(failure);
            Assert.Contains("Vulkan", unsupported.Message);
            Assert.True(
                unsupported.Message.Contains("Vulkan 1.4", StringComparison.Ordinal)
                || unsupported.Message.Contains("No device satisfies Vulkan Native requirements. Missing:", StringComparison.Ordinal),
                unsupported.Message);
        }
        finally { backend?.Dispose(); }
    }
}
