using Lumyte.UI.Application.Navigation;
using Xunit;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Navigation.Tests;

/// <summary>Checks rejected, canceled and retained transitions against the real standard host.</summary>
public sealed class TransitionTests
{
    /// <summary>Checks loading keeps the current publication, Busy is immediate and cancellation cleans the candidate.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task CanceledPreparationKeepsTheOldPageAndCleansItsOwnedLeaseAsync()
    {
        var provider = new PageProvider();
        UiPageKey<UiNoArgs> home = provider.Register("/", (_, _) => new(Panel()));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        UiPageKey<UiNoArgs> slow = provider.Register("/slow", async (activation, token) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return await activation.Context.Dispatcher.InvokeAsync(() => Panel());
        });
        await using var harness = new NavigationHarness(provider, navigation => navigation.RequestNavigate(home, default));
        await harness.ReadyAsync();
        UiNavigationWindowSnapshot before = await harness.App.Dispatcher.InvokeAsync(harness.Context.View.CaptureNavigationSnapshot);
        UiNavigationRequest request = await harness.App.Dispatcher.InvokeAsync(() => harness.Content.Navigation.RequestNavigate(slow, default));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        UiNavigationRequest busy = await harness.App.Dispatcher.InvokeAsync(() => harness.Content.Navigation.RequestNavigate(home, default));
        Assert.Equal(UiNavigationStatus.Busy, (await busy.Completion).Status);
        Assert.Equal(before, await harness.App.Dispatcher.InvokeAsync(harness.Context.View.CaptureNavigationSnapshot));
        request.Cancel();
        Assert.Equal(UiNavigationStatus.Canceled, (await request.Completion.WaitAsync(TimeSpan.FromSeconds(5))).Status);
        await harness.IdleAsync();
        Assert.Equal(before, await harness.App.Dispatcher.InvokeAsync(harness.Context.View.CaptureNavigationSnapshot));
        Assert.Equal(1, provider.Activations[1].Retires);
        Assert.Equal(1, provider.Activations[1].Disposals);
        Assert.Single((await harness.SnapshotAsync()).Entries);
    }

    /// <summary>Checks guard rejection and failure preserve history; recursive navigation never runs another factory.</summary>
    /// <param name="throwing">Whether the guard fails instead of rejecting.</param>
    /// <returns>The verification.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LeaveGuardPreservesPublicationAndRejectsReentryAsync(bool throwing)
    {
        var provider = new PageProvider();
        UiNavigationRequest? recursive = null;
        UiPageKey<UiNoArgs> home = provider.Register("/", (activation, _) =>
        {
            activation.LeaveGuard = new Guard(context =>
            {
                recursive = activation.Context.Navigation.RequestNavigate("/");
                return throwing ? throw new ArithmeticException("guard") : new(false, "unsaved");
            });
            return new(Panel());
        });
        UiPageKey<UiNoArgs> other = provider.Register("/other", (_, _) => new(Panel()));
        await using var harness = new NavigationHarness(provider, navigation => navigation.RequestNavigate(home, default), diagnostics: true);
        await harness.ReadyAsync();
        UiNavigationWindowSnapshot before = await harness.App.Dispatcher.InvokeAsync(harness.Context.View.CaptureNavigationSnapshot);
        UiNavigationRequest request = await harness.App.Dispatcher.InvokeAsync(() => harness.Content.Navigation.RequestNavigate(other, default));
        UiNavigationResult result = await request.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(throwing ? UiNavigationStatus.Failed : UiNavigationStatus.Rejected, result.Status);
        Assert.Equal(UiNavigationStatus.Busy, (await recursive!.Completion).Status);
        await harness.IdleAsync();
        Assert.Equal(before, await harness.App.Dispatcher.InvokeAsync(harness.Context.View.CaptureNavigationSnapshot));
        Assert.Equal(2, provider.Activations.Count);
        Assert.Equal(1, provider.Activations[1].Disposals);
    }

    /// <summary>Checks a committed result remains stable while failed retirement retains the old surface for retry.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task RetirementFailureBlocksNewNavigationUntilTheSameLeaseIsRetriedAsync()
    {
        var provider = new PageProvider();
        bool fail = true;
        UiPageKey<UiNoArgs> home = provider.Register("/", (activation, _) =>
        {
            activation.Retire = () => fail ? ValueTask.FromException(new ArithmeticException("retry")) : ValueTask.CompletedTask;
            return new(Panel());
        });
        UiPageKey<UiNoArgs> other = provider.Register("/other", (_, _) => new(Panel()));
        await using var harness = new NavigationHarness(provider, navigation => navigation.RequestNavigate(home, default));
        await harness.ReadyAsync();
        UiNavigationRequest request = await harness.App.Dispatcher.InvokeAsync(() => harness.Content.Navigation.RequestNavigate(other, default));
        UiNavigationResult result = await request.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(UiNavigationStatus.Committed, result.Status);
        await harness.WaitAsync(() => harness.Content.Navigation.CaptureSnapshot().Phase == UiNavigationPhase.RetirementBlocked);
        Assert.Equal(0, provider.Activations[0].Disposals);
        Assert.Equal(1, provider.Activations[0].Retires);
        UiNavigationRequest busy = await harness.App.Dispatcher.InvokeAsync(() => harness.Content.Navigation.RequestBack());
        Assert.Equal(UiNavigationStatus.Busy, (await busy.Completion).Status);
        fail = false;
        Task retry = await harness.App.Dispatcher.InvokeAsync(() => harness.Content.Navigation.RetryRetirementAsync().AsTask());
        await retry.WaitAsync(TimeSpan.FromSeconds(5));
        await harness.IdleAsync();
        Assert.Same(result, await request.Completion);
        Assert.Equal(1, provider.Activations[0].Disposals);
        Assert.Equal(2, provider.Activations[0].Retires);
    }

    /// <summary>Checks stopping an initial pending prepare does not wait on surface release to settle initialization.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task StopDuringInitialPreparationDoesNotCreateAnInitializationCycleAsync()
    {
        var provider = new PageProvider();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        UiPageKey<UiNoArgs> slow = provider.Register("/slow", async (activation, token) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return await activation.Context.Dispatcher.InvokeAsync(() => Panel());
        });
        var harness = new NavigationHarness(provider, navigation => navigation.RequestNavigate(slow, default));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await harness.App.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, provider.Activations[0].Retires);
        Assert.Equal(1, provider.Activations[0].Disposals);
        await harness.App.DisposeAsync();
    }

    /// <summary>Checks a settled callback can queue another transition after retirement without running its factory recursively.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task CompletionCallbackCanQueueTheNextPageAfterCleanupAsync()
    {
        var provider = new PageProvider();
        bool inCallback = false;
        UiPageKey<UiNoArgs> home = provider.Register("/", (_, _) => new(Panel()));
        UiPageKey<UiNoArgs> other = provider.Register("/other", (_, _) =>
        {
            Assert.False(inCallback);
            return new(Panel());
        });
        NavigationHarness? harness = null;
        UiNavigationRequest? followup = null;
        harness = new(provider, nav => nav.RequestNavigate(home, default), onCompleted: result =>
        {
            if (result.Status == UiNavigationStatus.Committed && followup is null)
            {
                inCallback = true;
                followup = harness!.Content.Navigation.RequestNavigate(other, default);
                inCallback = false;
            }
        });
        await using NavigationHarness owned = harness;
        await owned.ReadyAsync();
        await owned.WaitAsync(() => followup?.Completion.IsCompleted == true);
        Assert.Equal(UiNavigationStatus.Committed, (await followup!.Completion).Status);
        await owned.IdleAsync();
        Assert.Equal(2, provider.Activations.Count);
    }

    private sealed class Guard(Func<UiPageLeaveContext, UiPageLeaveDecision> check) : IUiPageLeaveGuard
    {
        public ValueTask<UiPageLeaveDecision> CanLeaveAsync(UiPageLeaveContext context, CancellationToken cancellationToken) => new(check(context));
    }
}
