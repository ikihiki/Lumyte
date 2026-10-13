using Lumyte.Graphics.Hosting;

namespace Lumyte.Resources.Graphics.Tests;

internal sealed class TestFactory(GraphicsTestState state) : IGraphicsDeviceFactory
{
    public ValueTask<IGraphicsDeviceOwner> CreateAsync(CancellationToken cancellationToken = default)
    {
        var device = new TestDevice();
        state.Devices.Add(device);
        return ValueTask.FromResult<IGraphicsDeviceOwner>(device);
    }
}
