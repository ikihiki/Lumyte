using Lumyte.Graphics.Tests;

namespace Lumyte.Graphics.WebGPU.Tests;

[CollectionDefinition("GpuBackend", DisableParallelization = true)]
public sealed class WebGpuTestCollection : ICollectionFixture<GpuBackendTestGate>;
