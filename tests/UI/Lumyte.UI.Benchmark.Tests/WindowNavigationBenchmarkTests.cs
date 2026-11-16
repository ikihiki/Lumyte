using Lumyte.UI.Application.Navigation.Benchmark;
using Lumyte.UI.Navigation;
using Lumyte.UI.Navigation.Benchmark;
using Lumyte.UI.Windowing.Benchmark;
using Xunit;

namespace Lumyte.UI.Benchmark.Tests;

/// <summary>Functional state and ownership oracles for current public CPU workloads.</summary>
public sealed class WindowNavigationBenchmarkTests
{
    /// <summary>Checks repeated independent menu surfaces, callbacks, focus and final drain.</summary>
    [Fact]
    public void HostedWindowsKeepFocusAndResourcesIsolated()
    {
        using var session = new HostedWindowSession();
        session.VerifyRoot();
        Assert.Equal(1, session.PopupCycle(false));
        Assert.Equal(3, session.PopupCycle(true));
        Assert.Equal(1, session.InputCycle());
        Assert.Equal(2, session.InputCycle());
        Assert.Equal(5, session.PopupCycle(false));
        Assert.Equal(session.Opened, session.Closed);
    }

    /// <summary>Checks the real public native adapter on a CPU protocol platform, not physical windows.</summary>
    [Fact]
    public void NativeAdapterDrainsProtocolLifetimes() => Assert.True(WindowSystemUiWindowManager.RunProtocol() > 0);

    /// <summary>Checks typed copies, bounded history, recreated activations, rejection and cancellation.</summary>
    [Fact]
    public void NavigatorPreservesHistoryAndOwnsEachActivationOnce()
    {
        using var session = new NavigationSession();
        for (int i = 0; i < 8; i++)
        {
            session.Navigate(UiNavigationMode.Push);
        }

        Assert.Equal(4, session.Snapshot.Entries.Count);
        Guid? before = session.Snapshot.ActivationId;
        session.BackForward();
        Assert.NotEqual(before, session.Snapshot.ActivationId);
        session.Navigate(UiNavigationMode.Replace);
        Assert.Equal(4, session.Snapshot.Entries.Count);
        Assert.Equal(UiNavigationStatus.Rejected, session.Reject());
        Assert.Equal(UiNavigationStatus.Canceled, session.Cancel());
        Assert.True(session.Disposals > 0);
    }

    /// <summary>Checks real DI scopes and observed frame advancement through finite page waves.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task RegisteredPagesDrainScopesAndObserveFramesAsync()
    {
        await using PageApplicationSession session = await PageApplicationSession.CreateAsync(false);
        int before = await session.StableFrameAsync();
        Assert.True(await session.MutateAsync() > before);
        for (int i = 0; i < 6; i++)
        {
            Assert.Equal(i + 2, await session.ReplaceAsync());
        }

        Assert.Equal(6, session.Disposals);
        Assert.Equal(2, session.NextFrames);
    }

    /// <summary>Checks actual motion admission without claiming GPU transition execution.</summary>
    /// <param name="disabled">Whether motion is disabled.</param>
    /// <returns>The verification.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TransitionAdmissionSeparatesLogicalAndVisualCompletionAsync(bool disabled)
    {
        await using PageApplicationSession session = await PageApplicationSession.CreateAsync(true, disabled);
        Assert.Equal(disabled ? "motion-disabled" : "capture-unavailable", await session.TransitionAsync());
        Assert.Equal(disabled ? "motion-disabled" : "capture-unavailable", await session.TransitionAsync());
        Assert.Equal(2, session.Disposals);
    }
}
