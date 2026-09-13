using Lumyte.Graphics.Tests;

namespace Lumyte.Graphics.WebGPU.Tests;

[CollectionDefinition("GpuBackend")]
public sealed class WebGpuTestCollection : ICollectionFixture<WebGpuTestGate>;

public sealed class WebGpuTestGate()
    : GpuBackendTestGate("Lumyte.Graphics.Tests.GpuBackend.WebGPU.Dawn");
