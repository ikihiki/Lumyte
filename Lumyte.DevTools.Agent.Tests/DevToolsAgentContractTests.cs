using MessagePack;
namespace Lumyte.DevTools.Agent.Tests;
public sealed class DevToolsAgentContractTests
{
    [Fact]
    public void RoundTripsExplicitMessagePackContract()
    {
        DevToolsHostRegistration expected = new("game", "Game", [new("demo", [new("get", "query", "Request", "Response")])]);
        DevToolsHostRegistration actual = MessagePackSerializer.Deserialize<DevToolsHostRegistration>(MessagePackSerializer.Serialize(expected));
        Assert.Equal(expected.HostId, actual.HostId);
        Assert.Equal(expected.DisplayName, actual.DisplayName);
        Assert.Collection(actual.Domains, domain =>
        {
            Assert.Equal("demo", domain.Name);
            Assert.Collection(domain.Features, feature => Assert.Equal(new DevToolsAgentFeature("get", "query", "Request", "Response"), feature));
        });
    }
}
