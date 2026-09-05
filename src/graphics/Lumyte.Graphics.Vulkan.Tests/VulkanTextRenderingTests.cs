using Lumyte.Graphics.Text.Tests;

namespace Lumyte.Graphics.Vulkan.Tests;

[Collection("GpuBackend")]
public sealed class VulkanTextRenderingTests : TextBackendConformanceTests
{
    protected override IGpuBackend CreateBackend() => VulkanDevice.Create();
}
