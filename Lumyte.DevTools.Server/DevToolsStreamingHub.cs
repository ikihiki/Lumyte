using Lumyte.DevTools.Agent;

using MagicOnion.Server.Hubs;

namespace Lumyte.DevTools.Server;

public sealed class DevToolsStreamingHub(DevToolsHostRegistry registry)
    : StreamingHubBase<IDevToolsAgentHub, IDevToolsAgentReceiver>, IDevToolsAgentHub
{
    private string ConnectionKey => ConnectionId.ToString("N");
    public ValueTask RegisterAsync(DevToolsHostRegistration registration) { registry.Attach(ConnectionKey, registration, Client); return ValueTask.CompletedTask; }
    public ValueTask CompleteInvocationAsync(DevToolsInvocationResponse response) { registry.Complete(ConnectionKey, response); return ValueTask.CompletedTask; }
    public ValueTask PublishEventAsync(DevToolsAgentEvent value) { registry.Publish(ConnectionKey, value); return ValueTask.CompletedTask; }
    protected override ValueTask OnDisconnected() { registry.Detach(ConnectionKey); return ValueTask.CompletedTask; }
}
