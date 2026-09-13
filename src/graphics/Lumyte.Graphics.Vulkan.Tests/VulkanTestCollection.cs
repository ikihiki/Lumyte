using Lumyte.Graphics.Tests;

namespace Lumyte.Graphics.Vulkan.Tests;

[CollectionDefinition("GpuBackend")]
public sealed class VulkanTestCollection : ICollectionFixture<VulkanGpuTestGate>;

public sealed class VulkanGpuTestGate() : GpuBackendTestGate(MutexName)
{
    public const string MutexName = "Lumyte.Graphics.Tests.GpuBackend.Vulkan";
}
