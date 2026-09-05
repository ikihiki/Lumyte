namespace Lumyte.DevTools.Tests;

public sealed class DevToolsHubTests
{
    private static readonly DevToolsDomain Input = new("input");
    private static readonly DevToolsQuery<string, int> GetLength = new("getLength");
    private static readonly DevToolsCommand<int, string> FormatValue = new("formatValue");
    private static readonly DevToolsEvent<int> ValueChanged = new("valueChanged");

    [Fact]
    public async Task QueryInvokesRegisteredHandler()
    {
        DevToolsHub hub = new();
        using IDisposable registration = hub.RegisterQuery(
            Input,
            GetLength,
            static (value, _) => ValueTask.FromResult(value.Length));

        int result = await hub.QueryAsync(Input, GetLength, "Lumyte");

        Assert.Equal(6, result);
    }

    [Fact]
    public async Task CommandInvokesRegisteredHandler()
    {
        DevToolsHub hub = new();
        using IDisposable registration = hub.RegisterCommand(
            Input,
            FormatValue,
            static (value, _) => ValueTask.FromResult($"value:{value}"));

        string result = await hub.CommandAsync(Input, FormatValue, 42);

        Assert.Equal("value:42", result);
    }

    [Fact]
    public async Task DisposingRegistrationRemovesFeatureAndDomain()
    {
        DevToolsHub hub = new();
        IDisposable registration = hub.RegisterQuery(
            Input,
            GetLength,
            static (value, _) => ValueTask.FromResult(value.Length));
        Assert.Single(hub.Domains);

        registration.Dispose();

        Assert.Empty(hub.Domains);
        await Assert.ThrowsAsync<DevToolsFeatureNotRegisteredException>(
            async () => await hub.QueryAsync(Input, GetLength, "value"));
    }

    [Fact]
    public void EnumeratesRegisteredDomainsAndFeatures()
    {
        DevToolsHub hub = new();
        using IDisposable query = hub.RegisterQuery(
            Input,
            GetLength,
            static (value, _) => ValueTask.FromResult(value.Length));
        using DevToolsEventPublisher<int> publisher = hub.RegisterEvent(Input, ValueChanged);

        Assert.Equal(Input, Assert.Single(hub.Domains));
        Assert.Collection(
            hub.GetFeatures(Input),
            feature => Assert.Equal(
                new DevToolsFeature("getLength", DevToolsFeatureKind.Query, typeof(string), typeof(int)),
                feature),
            feature => Assert.Equal(
                new DevToolsFeature("valueChanged", DevToolsFeatureKind.Event, typeof(int), null),
                feature));
    }

    [Fact]
    public void DuplicateRegistrationThrowsClearError()
    {
        DevToolsHub hub = new();
        using IDisposable registration = hub.RegisterQuery(
            Input,
            GetLength,
            static (value, _) => ValueTask.FromResult(value.Length));

        DevToolsFeatureAlreadyRegisteredException exception = Assert.Throws<DevToolsFeatureAlreadyRegisteredException>(
            () => hub.RegisterQuery(Input, GetLength, static (value, _) => ValueTask.FromResult(value.Length)));

        Assert.Contains("input/getLength", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EventPublishesToSubscribersUntilUnsubscribed()
    {
        DevToolsHub hub = new();
        using DevToolsEventPublisher<int> publisher = hub.RegisterEvent(Input, ValueChanged);
        List<int> received = [];
        IDisposable subscription = hub.Subscribe(
            Input,
            ValueChanged,
            (value, _) =>
            {
                received.Add(value);
                return ValueTask.CompletedTask;
            });

        await publisher.PublishAsync(1);
        subscription.Dispose();
        await publisher.PublishAsync(2);

        Assert.Equal([1], received);
    }

    [Fact]
    public async Task CancellationStopsQueryBeforeHandlerRuns()
    {
        DevToolsHub hub = new();
        bool invoked = false;
        using IDisposable registration = hub.RegisterQuery(
            Input,
            GetLength,
            (value, _) =>
            {
                invoked = true;
                return ValueTask.FromResult(value.Length);
            });
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await hub.QueryAsync(Input, GetLength, "value", cancellation.Token));
        Assert.False(invoked);
    }

    [Fact]
    public async Task CancellationIsPassedToHandlersAndListeners()
    {
        DevToolsHub hub = new();
        CancellationToken queryToken = default;
        CancellationToken eventToken = default;
        using IDisposable registration = hub.RegisterQuery(
            Input,
            GetLength,
            (value, token) =>
            {
                queryToken = token;
                return ValueTask.FromResult(value.Length);
            });
        using DevToolsEventPublisher<int> publisher = hub.RegisterEvent(Input, ValueChanged);
        using IDisposable subscription = hub.Subscribe(
            Input,
            ValueChanged,
            (value, token) =>
            {
                eventToken = token;
                return ValueTask.CompletedTask;
            });
        using CancellationTokenSource cancellation = new();

        await hub.QueryAsync(Input, GetLength, "value", cancellation.Token);
        await publisher.PublishAsync(1, cancellation.Token);

        Assert.Equal(cancellation.Token, queryToken);
        Assert.Equal(cancellation.Token, eventToken);
    }

    [Fact]
    public async Task MismatchedContractThrowsClearError()
    {
        DevToolsHub hub = new();
        using IDisposable registration = hub.RegisterQuery(
            Input,
            GetLength,
            static (value, _) => ValueTask.FromResult(value.Length));
        DevToolsQuery<int, string> mismatched = new(GetLength.Name);

        DevToolsContractMismatchException exception = await Assert.ThrowsAsync<DevToolsContractMismatchException>(
            async () => await hub.QueryAsync(Input, mismatched, 1));

        Assert.Contains("input/getLength", exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(string).FullName!, exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(int).FullName!, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SubscribingToUnregisteredEventThrowsClearError()
    {
        DevToolsHub hub = new();

        DevToolsFeatureNotRegisteredException exception = Assert.Throws<DevToolsFeatureNotRegisteredException>(
            () => hub.Subscribe(Input, ValueChanged, static (_, _) => ValueTask.CompletedTask));

        Assert.Contains("input/valueChanged", exception.Message, StringComparison.Ordinal);
    }
}
