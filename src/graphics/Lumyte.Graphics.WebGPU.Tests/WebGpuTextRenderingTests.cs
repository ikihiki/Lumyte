using Lumyte.Graphics.Text.Tests;

using LegacyWebGpuBackend = Lumyte.Graphics.WebGPU.Legacy.WebGpuBackend;

namespace Lumyte.Graphics.WebGPU.Tests;

[Collection("GpuBackend")]
public sealed class WebGpuTextRenderingTests : TextBackendConformanceTests
{
    protected override IGpuBackend CreateBackend() => LegacyWebGpuBackend.Create();
}
