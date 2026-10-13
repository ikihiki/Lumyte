using Lumyte.Graphics.Hosting;

namespace Lumyte.Resources.Graphics;

internal sealed class GraphicsResourceDependency(string id, IGraphicsDeviceService service) : IResourceRuntimeDependency
{
    public event EventHandler? Changed
    {
        add => service.StateChanged += value;
        remove => service.StateChanged -= value;
    }

    public string Id => id;

    public object Identity => service;

    public ResourceRuntimeSnapshot Snapshot
    {
        get
        {
            GraphicsDeviceState state = service.State;
            return new(state.Version, state.Status == GraphicsDeviceServiceStatus.Ready, state.InvalidatedThroughVersion);
        }
    }
}
