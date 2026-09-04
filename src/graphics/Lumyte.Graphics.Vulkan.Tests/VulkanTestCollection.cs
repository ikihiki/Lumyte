using Lumyte.Graphics.Tests;

namespace Lumyte.Graphics.Vulkan.Tests;

[CollectionDefinition("GpuBackend", DisableParallelization = true)]
public sealed class VulkanTestCollection : ICollectionFixture<GpuBackendTestGate>;
