using Lumyte.UI.Navigation;
using Xunit;

namespace Lumyte.UI.Application.Navigation.Tests;

/// <summary>Exercises actual DI scopes through standard host navigation and shutdown.</summary>
public sealed class LifetimeTests
{
    /// <summary>Checks constructor injection, shared context, fresh scopes on history and final disposal order.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task PageScopesAreIndependentAcrossHistoryAndShareTheApplicationSingletonAsync()
    {
        var fixture = new PageFixture();
        await fixture.ReadyAsync();
        PageVisit first = fixture.Probe.Visits[0];
        Assert.Same(first.Context, first.Service.Context);
        Assert.Same(fixture.Probe, first.Service.Probe);
        Assert.Equal(UiNavigationStatus.Committed, (await fixture.NavigateAsync("/next")).Status);
        await fixture.ReadyAsync();
        PageVisit next = fixture.Probe.Visits[1];
        Assert.NotSame(first.Service, next.Service);
        Assert.Equal(1, first.Disposes);
        Assert.Equal(1, first.Service.Disposes);
        Assert.True(first.RetiredOnOwner);
        UiNavigationRequest back = await fixture.App.Dispatcher.InvokeAsync(() => fixture.Navigator.RequestBack());
        await back.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        await fixture.ReadyAsync();
        Assert.Equal(3, fixture.Probe.Visits.Count);
        Assert.NotSame(first.Service, fixture.Probe.Visits[2].Service);
        await fixture.DisposeAsync();
        Assert.All(fixture.Probe.Visits, visit =>
        {
            Assert.Equal(1, visit.Prepares);
            Assert.Equal(1, visit.Builds);
            Assert.Equal(1, visit.Retires);
            Assert.Equal(1, visit.Disposes);
            Assert.True(visit.RetiredOnOwner);
            Assert.Equal(1, visit.Service.Disposes);
        });
    }

    /// <summary>Checks partial scope ownership survives each user-code failure and leaves the current view intact.</summary>
    /// <param name="stage">The failing user-code stage.</param>
    /// <returns>The verification.</returns>
    [Theory]
    [InlineData("factory")]
    [InlineData("constructor")]
    [InlineData("prepare")]
    [InlineData("build")]
    [InlineData("null")]
    public async Task FailedCandidatesReturnTheirScopesAndKeepTheCurrentPageAsync(string stage)
    {
        await using var fixture = new PageFixture((_, probe) => probe.FailureStage = stage);
        await fixture.ReadyAsync();
        Guid? activation = (await fixture.App.Dispatcher.InvokeAsync(fixture.Navigator.CaptureSnapshot)).ActivationId;
        Assert.Equal(UiNavigationStatus.Failed, (await fixture.NavigateAsync("/bad")).Status);
        await fixture.ReadyAsync();
        Assert.Equal(activation, (await fixture.App.Dispatcher.InvokeAsync(fixture.Navigator.CaptureSnapshot)).ActivationId);
        Assert.Equal(0, fixture.Probe.Visits[0].Retires);
        PageService candidate = fixture.Probe.Services[1];
        Assert.Equal(1, candidate.Disposes);
        Assert.Equal(0, fixture.Probe.Services[0].Disposes);
    }

    /// <summary>Checks canceled nonpublished pages are retired and never built after a late completion.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task CancellationReleasesThePreparedScopeWithoutPublishingAsync()
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var fixture = new PageFixture((_, probe) => probe.Preparation = ready);
        await fixture.ReadyAsync();
        UiNavigationRequest request = await fixture.App.Dispatcher.InvokeAsync(() => fixture.Navigator.RequestNavigate("/bad"));
        await fixture.WaitAsync(() => fixture.Probe.Visits.Count == 2);
        request.Cancel();
        Assert.Equal(UiNavigationStatus.Canceled, (await request.Completion.WaitAsync(TimeSpan.FromSeconds(10))).Status);
        ready.SetResult();
        await fixture.ReadyAsync();
        Assert.Equal(0, fixture.Probe.Visits[1].Builds);
        Assert.Equal(1, fixture.Probe.Visits[1].Retires);
        Assert.Equal(1, fixture.Probe.Services[1].Disposes);
    }

    /// <summary>Checks failed retirement preserves the same scope until its retry succeeds.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task RetirementFailureCanRetryBeforeScopeDisposalAsync()
    {
        await using var fixture = new PageFixture((_, probe) => probe.FailRetire = true);
        await fixture.ReadyAsync();
        Assert.Equal(UiNavigationStatus.Committed, (await fixture.NavigateAsync("/next")).Status);
        await fixture.WaitAsync(() => fixture.Navigator.CaptureSnapshot().Phase == UiNavigationPhase.RetirementBlocked);
        Assert.Equal(0, fixture.Probe.Services[0].Disposes);
        Task retry = await fixture.App.Dispatcher.InvokeAsync(() => fixture.Navigator.RetryRetirementAsync().AsTask());
        await retry.WaitAsync(TimeSpan.FromSeconds(10));
        await fixture.ReadyAsync();
        Assert.Equal(2, fixture.Probe.Visits[0].Retires);
        Assert.Equal(1, fixture.Probe.Services[0].Disposes);
    }

    /// <summary>Checks DI disposal failure is terminal and never invokes the failed scope a second time.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task ScopeDisposalFailureIsNotRetriedAndOtherScopesStillStopAsync()
    {
        var fixture = new PageFixture((_, probe) => probe.FailDispose = true);
        await fixture.ReadyAsync();
        Assert.Equal(UiNavigationStatus.Committed, (await fixture.NavigateAsync("/next")).Status);
        await fixture.WaitAsync(() => fixture.Navigator.CaptureSnapshot().Phase == UiNavigationPhase.Faulted);
        Assert.Equal(1, fixture.Probe.Services[0].Disposes);
        await Assert.ThrowsAsync<UiApplicationException>(() => fixture.App.StopAsync().WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.All(fixture.Probe.Services, service => Assert.Equal(1, service.Disposes));
        await Assert.ThrowsAsync<UiApplicationException>(() => fixture.App.DisposeAsync().AsTask());
        Assert.All(fixture.Probe.Services, service => Assert.Equal(1, service.Disposes));
    }

    /// <summary>Checks the current DI page's guard is borrowed and rejection releases only the candidate scope.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task GuardRejectionPreservesTheCurrentScopeAsync()
    {
        var guard = new RejectGuard();
        await using var fixture = new PageFixture((_, probe) => probe.Guard = guard);
        await fixture.ReadyAsync();
        Assert.Equal(UiNavigationStatus.Rejected, (await fixture.NavigateAsync("/next")).Status);
        await fixture.ReadyAsync();
        Assert.Equal(1, guard.Calls);
        Assert.Equal(0, fixture.Probe.Services[0].Disposes);
        Assert.Equal(1, fixture.Probe.Services[1].Disposes);
    }

    private sealed class RejectGuard : IUiPageLeaveGuard
    {
        internal int Calls { get; private set; }

        public ValueTask<UiPageLeaveDecision> CanLeaveAsync(UiPageLeaveContext context, CancellationToken cancellationToken)
        {
            Calls++;
            return ValueTask.FromResult(new UiPageLeaveDecision(false, "unsaved"));
        }
    }
}
