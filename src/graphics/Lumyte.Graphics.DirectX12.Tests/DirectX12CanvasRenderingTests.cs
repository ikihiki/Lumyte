using Lumyte.Graphics.TwoD.Tests;

namespace Lumyte.Graphics.DirectX12.Tests;

[Collection("GpuBackend")]
public sealed class DirectX12CanvasRenderingTests : BackendConformanceTests
{
    protected override IGpuBackend CreateBackend() => DirectX12Device.Create();
}
