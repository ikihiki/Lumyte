using Lumyte.Graphics.Tests;

namespace Lumyte.Graphics.DirectX12.Tests;

[CollectionDefinition("GpuBackend")]
public sealed class DirectX12TestCollection : ICollectionFixture<DirectX12GpuTestGate>;

public sealed class DirectX12GpuTestGate()
    : GpuBackendTestGate("Lumyte.Graphics.Tests.GpuBackend.DirectX12");
