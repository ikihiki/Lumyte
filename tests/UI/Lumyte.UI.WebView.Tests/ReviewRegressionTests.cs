using System.Reflection;
using Xunit;
using WebCompose = Lumyte.UI.WebView.Compose;

namespace Lumyte.UI.WebView.Tests;

/// <summary>Deterministic fake-engine regressions for review-reported ownership failures.</summary>
public sealed class ReviewRegressionTests
{
    /// <summary>A rejected replacement leaves the original declaration, owner and subscriptions intact.</summary>
    [Fact]
    public void RejectedSessionSwitchPreservesOldOwnershipAndSubscriptions()
    {
        UiWebViewSession old = Session();
        UiWebViewSession occupied = Session();
        using var surface = new UiSurface(WebCompose.WebView(session: old));
        using var other = new UiSurface(WebCompose.WebView(session: occupied));
        UiElement element = surface.RootElement;
        Action[] before = PixelSubscribers(old);
        Assert.Single(before);
        Assert.Throws<InvalidOperationException>(() =>
        {
            surface.Rebuild(WebCompose.WebView(session: occupied));
            surface.UpdateLayout();
        });
        Assert.Same(element, surface.RootElement);
        Assert.Same(old, Assert.IsType<WebCompose.Definitions.WebView>(surface.Root).Session);
        Assert.Equal(before, PixelSubscribers(old));
        Assert.Throws<InvalidOperationException>(() => old.Attach(new object()));
        Assert.Throws<InvalidOperationException>(() => occupied.Attach(new object()));
        old.NotifyPixelsChanged();
        surface.Dispose();
        Assert.Empty(PixelSubscribers(old));
    }

    /// <summary>Neither terminal closure nor faults permit implicit native restart.</summary>
    [Fact]
    public void TerminalStatesNeverResurrect()
    {
        UiWebViewSession session = Session();
        session.Update(UiWebViewState.Faulted, failureCode: "native-failed");
        session.Update(UiWebViewState.Ready);
        session.Update(UiWebViewState.Creating);
        Assert.Equal(UiWebViewState.Faulted, session.State);
        Assert.Equal("native-failed", session.FailureCode);
        session.Update(UiWebViewState.Closing);
        session.Update(UiWebViewState.Closed);
        int changed = 0;
        session.StateChanged += (_, _) => changed++;
        foreach (UiWebViewState state in Enum.GetValues<UiWebViewState>())
        {
            session.Update(state, new("https://example.invalid/stale"), loading: true);
        }

        Assert.Equal(UiWebViewState.Closed, session.State);
        Assert.False(session.IsLoading);
        Assert.Null(session.CurrentUri);
        Assert.Equal(0, changed);
    }

    /// <summary>Old load completion and address snapshots cannot replace a newer accepted navigation.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task StaleNavigationSnapshotCannotClearNewLoadingAsync()
    {
        UiWebViewSession session = Session();
        session.Update(UiWebViewState.Ready, sequence: 0);
        await session.NavigateAsync(new("https://example.invalid/first"));
        session.Update(UiWebViewState.Ready, new("https://example.invalid/first"), loading: true, sequence: 1);
        await session.NavigateAsync(new("https://example.invalid/second"));
        session.Update(UiWebViewState.Ready, new("https://example.invalid/second"), loading: true, sequence: 2);
        int changed = 0;
        session.StateChanged += (_, _) => changed++;
        session.Update(UiWebViewState.Ready, new("https://example.invalid/first"), loading: false, sequence: 1);
        Assert.True(session.IsLoading);
        Assert.Equal(new Uri("https://example.invalid/second"), session.CurrentUri);
        Assert.Equal(0, changed);
        session.Update(UiWebViewState.Ready, loading: false, sequence: 2);
        Assert.False(session.IsLoading);
    }

    /// <summary>Ambiguous dispatch failure faults once and never replays the remaining creation queue.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task FailedCreationFlushFaultsAndDropsRemainingCommandsAsync()
    {
        int attempted = 0;
        var session = new UiWebViewSession(new("https://example.invalid/"), () => { }, _ =>
        {
            attempted++;
            throw new InvalidOperationException("fake-send-failed");
        });
        await session.ReloadAsync();
        await session.GoBackAsync();
        int notifications = 0;
        session.StateChanged += (_, _) =>
        {
            Assert.Equal(UiWebViewState.Faulted, session.State);
            Assert.Equal("command-dispatch-failed", session.FailureCode);
            notifications++;
        };
        Assert.Throws<InvalidOperationException>(() => session.Update(UiWebViewState.Ready));
        Assert.Equal(1, notifications);
        session.Update(UiWebViewState.Ready);
        Assert.Throws<InvalidOperationException>(() => session.ReloadAsync());
        Assert.Equal(1, attempted);
    }

    private static UiWebViewSession Session() => new(new("https://example.invalid/"), () => { }, _ => { });

    private static Action[] PixelSubscribers(UiWebViewSession session) =>
        ((Action?)typeof(UiWebViewSession).GetField("PixelsChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session))?.GetInvocationList().Cast<Action>().ToArray() ?? [];
}
