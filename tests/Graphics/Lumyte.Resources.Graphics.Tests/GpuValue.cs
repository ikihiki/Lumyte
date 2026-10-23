using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Hosting;

namespace Lumyte.Resources.Graphics.Tests;

internal sealed class GpuValue(GraphicsResourceRuntime runtime, GraphicsDeviceLease lease, IGraphicDevice device)
{
    internal GraphicsResourceRuntime Runtime { get; } = runtime;

    internal GraphicsDeviceLease Lease { get; } = lease;

    internal IGraphicDevice Device { get; } = device;

    internal bool Released { get; set; }
}
