using Lumyte.Graphics.TwoD.Tests;

using LegacyWebGpuBackend = Lumyte.Graphics.WebGPU.Legacy.WebGpuBackend;

namespace Lumyte.Graphics.WebGPU.Tests;

[Collection("GpuBackend")]
public sealed class WebGpuCanvasRenderingTests : BackendConformanceTests
{
    protected override IGpuBackend CreateBackend() => LegacyWebGpuBackend.Create();
}
