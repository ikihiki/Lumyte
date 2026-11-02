using Lumyte.UI.Application;
using Xunit;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Navigation.Tests;

/// <summary>Checks final disposal and owner dispatch after asynchronous retirement and stop.</summary>
public sealed class LifetimeTests
{
    /// <summary>Checks cleanup can explicitly return from ConfigureAwait(false) after ordinary admission has closed.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task CleanupDispatchRemainsAvailableOnlyDuringAwaitedRetirementAsync()
    {
        var provider = new PageProvider();
        bool cleanupOnOwner = false;
        UiPageKey<UiNoArgs> key = provider.Register("/", (activation, _) =>
        {
            activation.Retire = async () =>
            {
                await Task.Delay(1).ConfigureAwait(false);
                await activation.Context.Dispatcher.InvokeAsync(() => cleanupOnOwner = activation.Context.Dispatcher.CheckAccess());
            };
            return new(Panel());
        });
        var harness = new NavigationHarness(provider, nav => nav.RequestNavigate(key, default));
        await harness.ReadyAsync();
        await harness.DisposeAsync();
        Assert.True(cleanupOnOwner);
        Assert.Equal(1, provider.Activations[0].Retires);
        Assert.Equal(1, provider.Activations[0].Disposals);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => provider.Activations[0].Context.Dispatcher.InvokeAsync(() => { }).AsTask());
    }

    /// <summary>Checks a failed final scope disposal is never retried while the remaining active page can still stop.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task TerminalDisposalFailureDoesNotDisposeTheSameLeaseTwiceAsync()
    {
        var provider = new PageProvider();
        UiPageKey<UiNoArgs> home = provider.Register("/", (activation, _) =>
        {
            activation.Dispose = () => ValueTask.FromException(new ArithmeticException("final release"));
            return new(Panel());
        });
        UiPageKey<UiNoArgs> other = provider.Register("/other", (_, _) => new(Panel()));
        var harness = new NavigationHarness(provider, nav => nav.RequestNavigate(home, default));
        await harness.ReadyAsync();
        UiNavigationRequest request = await harness.App.Dispatcher.InvokeAsync(() => harness.Content.Navigation.RequestNavigate(other, default));
        Assert.Equal(UiNavigationStatus.Committed, (await request.Completion).Status);
        await harness.WaitAsync(() => harness.Content.Navigation.CaptureSnapshot().Phase == UiNavigationPhase.Faulted);
        Assert.Equal(1, provider.Activations[0].Disposals);
        await Assert.ThrowsAsync<UiApplicationException>(() => harness.App.StopAsync().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, provider.Activations[0].Disposals);
        Assert.Equal(1, provider.Activations[1].Disposals);
        Assert.Equal(UiApplicationState.Failed, harness.App.State);
        await Assert.ThrowsAsync<UiApplicationException>(() => harness.App.DisposeAsync().AsTask());
    }
}
