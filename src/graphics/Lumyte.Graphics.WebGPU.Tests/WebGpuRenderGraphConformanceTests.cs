using Lumyte.Graphics.Tests;
using Lumyte.Graphics.RenderGraph;

using LegacyWebGpuBackend = Lumyte.Graphics.WebGPU.Legacy.WebGpuBackend;

namespace Lumyte.Graphics.WebGPU.Tests;

[Collection("GpuBackend")]
public sealed class WebGpuRenderGraphConformanceTests : GpuRenderGraphBackendConformanceTests
{
    protected override IGpuBackend CreateBackend() => LegacyWebGpuBackend.Create();
}
