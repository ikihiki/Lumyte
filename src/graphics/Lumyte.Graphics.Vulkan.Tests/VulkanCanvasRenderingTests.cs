using Lumyte.Graphics.TwoD.Tests;

namespace Lumyte.Graphics.Vulkan.Tests;

[Collection("GpuBackend")]
public sealed class VulkanCanvasRenderingTests : BackendConformanceTests
{
    protected override IGpuBackend CreateBackend() => VulkanDevice.Create();
}
