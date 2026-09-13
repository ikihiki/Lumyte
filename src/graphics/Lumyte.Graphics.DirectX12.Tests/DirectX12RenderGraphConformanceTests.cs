using Lumyte.Graphics.Tests;
using Lumyte.Graphics.RenderGraph.Legacy;

namespace Lumyte.Graphics.DirectX12.Tests;

[Collection("GpuBackend")]
public sealed class DirectX12RenderGraphConformanceTests : GpuRenderGraphBackendConformanceTests
{
    protected override IGpuBackend CreateBackend() => DirectX12Device.Create();
}
