using Lumyte.Graphics.Text.Tests;

namespace Lumyte.Graphics.WebGPU.Tests;

[Collection("GpuBackend")]
public sealed class WebGpuTextRenderingTests : TextBackendConformanceTests
{
    protected override IGpuBackend CreateBackend() => WebGpuBackend.Create();
}
