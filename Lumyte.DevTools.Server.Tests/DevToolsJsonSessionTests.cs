using System.Text.Json;

namespace Lumyte.DevTools.Server.Tests;

public sealed class DevToolsJsonSessionTests
{
    private static readonly DevToolsDomain Demo = new("demo");
    private static readonly DevToolsQuery<GetRequest, State> Get = new("get");
    private static readonly DevToolsCommand<ChangeRequest, State> Change = new("change");
    private static readonly DevToolsEvent<State> Changed = new("changed");

    [Fact]
    public async Task ListsDomainsAndTypedFeatures()
    {
        DevToolsHub hub = new();
        using IDisposable query = hub.RegisterQuery(Demo, Get, static (_, _) => ValueTask.FromResult(new State(1)));
        using DevToolsJsonSession session = new(hub, static (_, _) => ValueTask.CompletedTask);

        string response = await session.ProcessAsync("""{"id":1,"method":"domains"}""");
        using var document = JsonDocument.Parse(response);
        JsonElement domain = Assert.Single(document.RootElement.GetProperty("result").EnumerateArray());
        JsonElement feature = Assert.Single(domain.GetProperty("features").EnumerateArray());

        Assert.Equal("demo", domain.GetProperty("name").GetString());
        Assert.Equal("get", feature.GetProperty("name").GetString());
        Assert.Equal("query", feature.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task InvokesQueryAndCommandFromJson()
    {
        DevToolsHub hub = new();
        using IDisposable query = hub.RegisterQuery(Demo, Get, static (_, _) => ValueTask.FromResult(new State(3)));
        using IDisposable command = hub.RegisterCommand(Demo, Change, static (request, _) => ValueTask.FromResult(new State(request.Delta)));
        using DevToolsJsonSession session = new(hub, static (_, _) => ValueTask.CompletedTask);

        string queryResponse = await session.ProcessAsync("""{"id":"q","method":"invoke","domain":"demo","feature":"get","kind":"query","params":{}}""");
        string commandResponse = await session.ProcessAsync("""{"id":"c","method":"invoke","domain":"demo","feature":"change","kind":"command","params":{"delta":4}}""");

        Assert.Equal(3, JsonDocument.Parse(queryResponse).RootElement.GetProperty("result").GetProperty("value").GetInt32());
        Assert.Equal(4, JsonDocument.Parse(commandResponse).RootElement.GetProperty("result").GetProperty("value").GetInt32());
    }

    [Fact]
    public async Task SubscriptionForwardsPublishedEventAndCanUnsubscribe()
    {
        DevToolsHub hub = new();
        using DevToolsEventPublisher<State> publisher = hub.RegisterEvent(Demo, Changed);
        List<string> messages = [];
        using DevToolsJsonSession session = new(hub, (message, _) =>
        {
            messages.Add(message);
            return ValueTask.CompletedTask;
        });
        string subscribeResponse = await session.ProcessAsync("""{"id":1,"method":"subscribe","domain":"demo","feature":"changed"}""");
        string subscriptionId = JsonDocument.Parse(subscribeResponse).RootElement.GetProperty("result").GetProperty("subscriptionId").GetString()!;

        await publisher.PublishAsync(new State(7));
        await session.ProcessAsync($$"""{"id":2,"method":"unsubscribe","subscriptionId":"{{subscriptionId}}"}""");
        await publisher.PublishAsync(new State(8));

        JsonElement notification = Assert.Single(messages.Select(message => JsonDocument.Parse(message).RootElement.Clone()));
        Assert.Equal("demo", notification.GetProperty("event").GetProperty("domain").GetString());
        Assert.Equal(7, notification.GetProperty("event").GetProperty("params").GetProperty("value").GetInt32());
    }

    [Theory]
    [InlineData("not json", "invalid_json")]
    [InlineData("{\"id\":1,\"method\":\"missing\"}", "method_not_found")]
    [InlineData("{\"id\":1,\"method\":\"invoke\",\"domain\":\"demo\",\"feature\":\"missing\",\"kind\":\"query\",\"params\":{}}", "feature_not_found")]
    [InlineData("{\"id\":1,\"method\":\"invoke\",\"domain\":\"demo\",\"feature\":\"change\",\"kind\":\"command\",\"params\":{\"delta\":\"wrong\"}}", "invalid_params")]
    public async Task ReturnsStructuredErrorsWithoutEndingSession(string request, string expectedCode)
    {
        DevToolsHub hub = new();
        using IDisposable command = hub.RegisterCommand(Demo, Change, static (value, _) => ValueTask.FromResult(new State(value.Delta)));
        using DevToolsJsonSession session = new(hub, static (_, _) => ValueTask.CompletedTask);

        string error = await session.ProcessAsync(request);
        string success = await session.ProcessAsync("""{"id":2,"method":"domains"}""");

        Assert.Equal(expectedCode, JsonDocument.Parse(error).RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.True(JsonDocument.Parse(success).RootElement.TryGetProperty("result", out _));
    }

    public sealed record GetRequest;
    public sealed record ChangeRequest(int Delta);
    public sealed record State(int Value);
}
