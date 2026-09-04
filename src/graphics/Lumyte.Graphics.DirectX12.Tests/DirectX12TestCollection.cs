using Lumyte.Graphics.Tests;

namespace Lumyte.Graphics.DirectX12.Tests;

[CollectionDefinition("GpuBackend", DisableParallelization = true)]
public sealed class DirectX12TestCollection : ICollectionFixture<GpuBackendTestGate>;
