using System.Text.Json;
namespace Lumyte.DevTools.Server.Tests;
public sealed class RemoteProtocolNegotiationTests
{
    [Fact]
    public async Task NegotiatesSupportedVersionWithoutConnectedHost()
    {
        using DevToolsRemoteJsonSession session = new(new DevToolsHostRegistry(), static (_, _) => ValueTask.CompletedTask);

        string response = await session.ProcessAsync("""{"id":1,"method":"negotiate","protocolVersion":"1.0","capabilities":["subscriptions"]}""");
        using JsonDocument document = JsonDocument.Parse(response);

        JsonElement result = document.RootElement.GetProperty("result");
        Assert.Equal("1.0", result.GetProperty("protocolVersion").GetString());
        Assert.Contains(result.GetProperty("capabilities").EnumerateArray(), value => value.GetString() == "diagnostics-v1");
    }

    [Fact]
    public async Task RejectsUnknownProtocolVersionStructurally()
    {
        using DevToolsRemoteJsonSession session = new(new DevToolsHostRegistry(), static (_, _) => ValueTask.CompletedTask);

        string response = await session.ProcessAsync("""{"id":1,"method":"negotiate","protocolVersion":"99.0"}""");
        using JsonDocument document = JsonDocument.Parse(response);

        JsonElement error = document.RootElement.GetProperty("error");
        Assert.Equal("incompatible_protocol", error.GetProperty("code").GetString());
        Assert.False(error.GetProperty("retryable").GetBoolean());
    }
}
