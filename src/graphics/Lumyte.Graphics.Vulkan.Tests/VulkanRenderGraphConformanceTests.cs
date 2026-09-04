using Lumyte.Graphics.Tests;
using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Vulkan.Tests;

[Collection("GpuBackend")]
public sealed class VulkanRenderGraphConformanceTests : GpuRenderGraphBackendConformanceTests
{
    protected override IGpuBackend CreateBackend() => VulkanDevice.Create();
}
