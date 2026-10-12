namespace Lumyte.Graphics.Hosting.Tests;

internal sealed class TestFactory(TestFactoryState state) : IGraphicsDeviceFactory
{
    public async ValueTask<IGraphicsDeviceOwner> CreateAsync(CancellationToken cancellationToken = default)
    {
        state.Calls++;
        if (state.CreationWait is { } wait)
        {
            await wait.Task.WaitAsync(cancellationToken);
        }

        if (state.Failures > 0)
        {
            state.Failures--;
            throw new InvalidOperationException("Injected creation failure.");
        }

        var device = new TestDevice();
        state.Devices.Add(device);
        if (state.ReturnLost)
        {
            device.Lose();
        }

        return device;
    }
}
