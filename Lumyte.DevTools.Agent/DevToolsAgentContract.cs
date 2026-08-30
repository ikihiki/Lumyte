using MagicOnion;
using MessagePack;
namespace Lumyte.DevTools.Agent;
public interface IDevToolsAgentHub : IStreamingHub<IDevToolsAgentHub, IDevToolsAgentReceiver>
{
    ValueTask RegisterAsync(DevToolsHostRegistration registration);
    ValueTask CompleteInvocationAsync(DevToolsInvocationResponse response);
    ValueTask PublishEventAsync(DevToolsAgentEvent value);
}
public interface IDevToolsAgentReceiver { void Invoke(DevToolsInvocation invocation); }
[MessagePackObject] public sealed record DevToolsHostRegistration([property:Key(0)] string HostId,[property:Key(1)] string DisplayName,[property:Key(2)] DevToolsAgentDomain[] Domains);
[MessagePackObject] public sealed record DevToolsAgentDomain([property:Key(0)] string Name,[property:Key(1)] DevToolsAgentFeature[] Features);
[MessagePackObject] public sealed record DevToolsAgentFeature([property:Key(0)] string Name,[property:Key(1)] string Kind,[property:Key(2)] string RequestType,[property:Key(3)] string? ResponseType);
[MessagePackObject] public sealed record DevToolsInvocation([property:Key(0)] string Id,[property:Key(1)] string Domain,[property:Key(2)] string Feature,[property:Key(3)] string Kind,[property:Key(4)] byte[] Params);
[MessagePackObject] public sealed record DevToolsInvocationResponse([property:Key(0)] string Id,[property:Key(1)] byte[]? Result,[property:Key(2)] string? Error);
[MessagePackObject] public sealed record DevToolsAgentEvent([property:Key(0)] string Domain,[property:Key(1)] string Feature,[property:Key(2)] byte[] Params);
