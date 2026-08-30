namespace Lumyte.DevTools.Host.Tests;

public sealed class DemoCounterDomainTests
{
    [Fact]
    public async Task RegistersQueryCommandAndChangeEventAsOneDomain()
    {
        DevToolsHub hub = new();
        using DemoCounterDomain demo = new(hub);
        List<CounterState> events = [];
        using IDisposable subscription = hub.Subscribe(
            DemoCounterDomain.Domain,
            DemoCounterDomain.CounterChanged,
            (state, _) =>
            {
                events.Add(state);
                return ValueTask.CompletedTask;
            });

        CounterState initial = await hub.QueryAsync(
            DemoCounterDomain.Domain,
            DemoCounterDomain.GetCounter,
            new GetCounterRequest());
        CounterState changed = await hub.CommandAsync(
            DemoCounterDomain.Domain,
            DemoCounterDomain.ChangeCounter,
            new ChangeCounterRequest(2));

        Assert.Equal(new CounterState(0), initial);
        Assert.Equal(new CounterState(2), changed);
        Assert.Equal([changed], events);
        Assert.Collection(
            hub.GetFeatures(DemoCounterDomain.Domain),
            feature => Assert.Equal(DevToolsFeatureKind.Query, feature.Kind),
            feature => Assert.Equal(DevToolsFeatureKind.Command, feature.Kind),
            feature => Assert.Equal(DevToolsFeatureKind.Event, feature.Kind));
    }

    [Fact]
    public async Task DisposingDemoRemovesItsDomain()
    {
        DevToolsHub hub = new();
        DemoCounterDomain demo = new(hub);

        demo.Dispose();

        Assert.Empty(hub.Domains);
        await Assert.ThrowsAsync<DevToolsFeatureNotRegisteredException>(
            async () => await hub.QueryAsync(
                DemoCounterDomain.Domain,
                DemoCounterDomain.GetCounter,
                new GetCounterRequest()));
    }
}
