using Lumyte.Graphics.Tests;
using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.WebGPU.Tests;

[Collection("GpuBackend")]
public sealed class WebGpuRenderGraphConformanceTests : GpuRenderGraphBackendConformanceTests
{
    protected override IGpuBackend CreateBackend() => WebGpuBackend.Create();
}
