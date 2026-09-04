using Lumyte.Graphics.TwoD.Tests;

namespace Lumyte.Graphics.WebGPU.Tests;

[Collection("GpuBackend")]
public sealed class WebGpuCanvasRenderingTests : BackendConformanceTests
{
    protected override IGpuBackend CreateBackend() => WebGpuBackend.Create();
}
