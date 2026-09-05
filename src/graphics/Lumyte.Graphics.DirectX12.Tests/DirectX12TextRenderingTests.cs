using Lumyte.Graphics.Text.Tests;

namespace Lumyte.Graphics.DirectX12.Tests;

[Collection("GpuBackend")]
public sealed class DirectX12TextRenderingTests : TextBackendConformanceTests
{
    protected override IGpuBackend CreateBackend() => DirectX12Device.Create();
}
