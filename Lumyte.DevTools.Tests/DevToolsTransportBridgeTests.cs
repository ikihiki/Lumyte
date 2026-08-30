namespace Lumyte.DevTools.Tests;

public sealed class DevToolsTransportBridgeTests
{
    private static readonly DevToolsDomain Demo = new("demo");
    private static readonly DevToolsCommand<ChangeRequest, State> Change = new("change");
    private static readonly DevToolsEvent<State> Changed = new("changed");

    [Fact]
    public async Task UntypedInvocationUsesRegisteredTypedHandler()
    {
        DevToolsHub hub = new();
        using IDisposable registration = hub.RegisterCommand(
            Demo,
            Change,
            static (request, _) => ValueTask.FromResult(new State(request.Delta)));

        object? result = await hub.InvokeAsync(
            Demo,
            DevToolsFeatureKind.Command,
            Change.Name,
            new ChangeRequest(3));

        Assert.Equal(new State(3), Assert.IsType<State>(result));
    }

    [Fact]
    public async Task UntypedSubscriptionReceivesTypedEvent()
    {
        DevToolsHub hub = new();
        using DevToolsEventPublisher<State> publisher = hub.RegisterEvent(Demo, Changed);
        object? received = null;
        using IDisposable subscription = hub.Subscribe(
            Demo,
            Changed.Name,
            (value, _) =>
            {
                received = value;
                return ValueTask.CompletedTask;
            });

        await publisher.PublishAsync(new State(4));

        Assert.Equal(new State(4), Assert.IsType<State>(received));
    }

    private sealed record ChangeRequest(int Delta);
    private sealed record State(int Value);
}
