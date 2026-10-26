namespace Lumyte.Resources.Graphics.AotSmoke;

internal sealed class SmokeFactory(SmokeDevice device) : IGraphicsDeviceFactory
{
    public ValueTask<IGraphicsDeviceOwner> CreateAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult<IGraphicsDeviceOwner>(device);
}
