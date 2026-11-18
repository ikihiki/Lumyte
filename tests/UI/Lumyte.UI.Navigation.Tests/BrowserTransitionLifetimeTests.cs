using Lumyte.UI.Windowing;
using Xunit;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Navigation.Tests;

/// <summary>Checks scope retention at the visual boundary without any browser or graphics backend.</summary>
public sealed class BrowserTransitionLifetimeTests
{
    /// <summary>Visual completion, history replacement, skip and shutdown release each page exactly once.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task PageLeaseSurvivesFlightThenHistoryAndShutdownDisposeExactlyOnceAsync()
    {
        var provider = new PageProvider();
        UiPageKey<UiNoArgs> home = provider.Register("/", (_, _) => new(Panel()));
        UiPageKey<UiNoArgs> next = provider.Register("/details", (_, _) => new(Panel()));
        var host = new Host();
        var navigation = new UiNavigator(provider, host);
        void Commit(UiNavigationRequest request)
        {
            for (int stage = 0; stage < 6; stage++)
            {
                navigation.Advance();
            }

            Assert.True(request.Completion.IsCompletedSuccessfully);
        }

        async Task Settle(UiNavigationRequest request, int disposed)
        {
            Commit(request);
            Assert.Equal(UiNavigationStatus.Committed, (await request.Completion).Status);
            Assert.False(request.VisualCompletion.IsCompleted);
            Assert.Equal(disposed, provider.Activations.Sum(page => page.Disposals));
            Assert.Equal(UiNavigationPhase.Animating, navigation.CaptureSnapshot().Phase);
            host.Current.Complete();
            await request.VisualCompletion;
            navigation.Advance();
            Assert.Equal(UiNavigationPhase.Idle, navigation.CaptureSnapshot().Phase);
        }

        await Settle(navigation.RequestNavigate(home, default), 0);
        await Settle(navigation.RequestNavigate(next, default), 0);
        Assert.Equal(1, provider.Activations[0].Disposals);
        await Settle(navigation.RequestBack(), 1);
        Assert.Equal(1, provider.Activations[1].Disposals);
        UiNavigationRequest forward = navigation.RequestForward();
        Commit(forward);
        Assert.Equal(UiNavigationStatus.Committed, (await forward.Completion).Status);
        Assert.Equal(0, provider.Activations[2].Disposals);
        forward.SkipAnimation();
        host.Current.Complete(skipped: true);
        Assert.Equal(new(UiVisualTransitionStatus.Skipped, "skipped-by-request"), await forward.VisualCompletion);
        navigation.Advance();
        Assert.Equal(1, provider.Activations[2].Disposals);
        navigation.BeginStop();
        await navigation.RetireAsync();
        await navigation.ReleaseAsync();
        Assert.Equal(4, provider.Activations.Count);
        Assert.All(provider.Activations, page => Assert.Equal(1, page.Disposals));
    }

    private sealed class Host : IUiNavigationHost, IUiDispatcher
    {
        public UiWindowId Window => default;

        public IUiDispatcher Dispatcher => this;

        public Change Current { get; private set; } = null!;

        public Guid? CaptureInputOperation() => null;

        public IUiNavigationViewChange BeginChange(Guid activationId) => Current = new();

        public bool CheckAccess() => true;

        public ValueTask InvokeAsync(Action action, CancellationToken cancellationToken = default)
        {
            action();
            return ValueTask.CompletedTask;
        }

        public ValueTask<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken = default) => ValueTask.FromResult(action());
    }

    private sealed class Change : IUiNavigationViewChange, IUiNavigationVisualChange
    {
        private readonly TaskCompletionSource<UiVisualTransitionResult> _visual = new();

        public void Complete(bool skipped = false) => _visual.SetResult(skipped ? new(UiVisualTransitionStatus.Skipped, "skipped-by-request") : new(UiVisualTransitionStatus.Completed, "completed"));

        public ValueTask PrepareAsync(Widget root, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask QuiesceAsync(Guid? originatingInputOperation, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public bool TryValidateCommit(out string code)
        {
            code = string.Empty;
            return true;
        }

        public void Commit(UiNavigationPublication publication)
        {
        }

        public ValueTask DrainRetiredAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask ReleaseRetiredAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask DrainCandidateAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask RunPageCleanupAsync(Func<CancellationToken, ValueTask> cleanup, CancellationToken cancellationToken) => cleanup(cancellationToken);

        public ValueTask DiscardAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public ValueTask PrepareVisualsAsync(UiPageTransitions? transitions, Func<bool> skipRequested, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask<UiVisualTransitionResult> RunVisualsAsync() => new(_visual.Task);

        public void StopVisuals() => _visual.TrySetResult(new(UiVisualTransitionStatus.Skipped, "stopped"));
    }
}
