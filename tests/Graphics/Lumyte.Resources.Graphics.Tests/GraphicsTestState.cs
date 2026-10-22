using Lumyte.Graphics.Hosting;

namespace Lumyte.Resources.Graphics.Tests;

internal sealed class GraphicsTestState
{
    internal List<TestDevice> Devices { get; } = [];

    internal List<GpuValue> Values { get; } = [];

    internal int CpuBuilds { get; set; }

    internal int ReleaseFailures { get; set; }

    internal TaskCompletionSource? BuildStarted { get; set; }

    internal TaskCompletionSource? BuildWait { get; set; }

    internal IGraphicsDeviceService? OverrideService { get; set; }
}
