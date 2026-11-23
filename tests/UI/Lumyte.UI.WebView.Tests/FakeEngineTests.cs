using Xunit;
using WebViewCompose = Lumyte.UI.WebView.Compose;

namespace Lumyte.UI.WebView.Tests;

/// <summary>Checks deterministic owner/engine boundaries without a browser, GPU or network.</summary>
public sealed class FakeEngineTests
{
    /// <summary>Commands are accepted in order and cancellation after acceptance does not undo them.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task ReadyCommandsPreserveKindSequenceAndAcceptedCancellationAsync()
    {
        var sent = new List<WebViewCommand>();
        var session = new UiWebViewSession(new("https://example.invalid/"), () => { }, sent.Add);
        session.Update(UiWebViewState.Ready);
        using var cancellation = new CancellationTokenSource();
        await session.NavigateAsync(new("http://example.invalid/page"), cancellation.Token);
        cancellation.Cancel();
        await session.GoBackAsync();
        await session.GoForwardAsync();
        await session.ReloadAsync();
        await session.StopLoadingAsync();
        Assert.Equal(new[] { WebViewCommandKind.Navigate, WebViewCommandKind.Back, WebViewCommandKind.Forward, WebViewCommandKind.Reload, WebViewCommandKind.Stop }, sent.Select(command => command.Kind));
        Assert.Equal(Enumerable.Range(1, 5).Select(value => (long)value), sent.Select(command => command.Sequence));
        Assert.Equal(new Uri("http://example.invalid/page"), sent[0].Uri);
        Assert.All(sent.Skip(1), command => Assert.Null(command.Uri));
    }

    /// <summary>Owner checks protect commands and callback application before side effects.</summary>
    [Fact]
    public void OwnerRejectionDoesNotSendOrMutateState()
    {
        bool owner = true;
        var sent = new List<WebViewCommand>();
        void CheckOwner()
        {
            if (!owner)
            {
                throw new InvalidOperationException("wrong-owner");
            }
        }

        var session = new UiWebViewSession(new("https://example.invalid/"), CheckOwner, sent.Add);
        owner = false;
        Assert.Throws<InvalidOperationException>(() => session.ReloadAsync());
        Assert.Throws<InvalidOperationException>(() => session.Update(UiWebViewState.Ready));
        Assert.Throws<InvalidOperationException>(() => session.Attach(new object()));
        Assert.Equal(UiWebViewState.Creating, session.State);
        Assert.Empty(sent);
    }

    /// <summary>Callbacks observe the updated snapshot, not a partially applied state.</summary>
    [Fact]
    public void StateNotificationExposesAppliedSnapshot()
    {
        var session = new UiWebViewSession(new("https://example.invalid/"), () => { }, _ => { });
        int notifications = 0;
        session.StateChanged += (sender, _) =>
        {
            Assert.Same(session, sender);
            Assert.Equal(UiWebViewState.Ready, session.State);
            Assert.Equal(new Uri("https://example.invalid/loaded"), session.CurrentUri);
            Assert.True(session.IsLoading);
            Assert.True(session.CanGoBack);
            Assert.False(session.CanGoForward);
            notifications++;
        };
        session.Update(UiWebViewState.Ready, new("https://example.invalid/loaded"), loading: true, back: true);
        Assert.Equal(1, notifications);
        Assert.False(session.ImeAvailable);
    }

    /// <summary>Fault discards creation work and closes without replaying it.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task FaultRejectsCommandsAndRetiresQueuedWorkAsync()
    {
        var sent = new List<WebViewCommand>();
        var session = new UiWebViewSession(new("https://example.invalid/"), () => { }, sent.Add);
        await session.ReloadAsync();
        session.Update(UiWebViewState.Faulted, failureCode: "renderer-exited");
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => session.StopLoadingAsync().AsTask());
        Assert.Equal("renderer-exited", error.Message);
        session.Update(UiWebViewState.Closing);
        session.Update(UiWebViewState.Closed);
        session.Update(UiWebViewState.Ready);
        Assert.Equal(UiWebViewState.Closed, session.State);
        Assert.Empty(sent);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.ReloadAsync().AsTask());
    }

    /// <summary>Live navigation validates schemes before reaching the fake engine.</summary>
    /// <param name="address">The denied address.</param>
    [Theory]
    [InlineData("file:///private")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,hello")]
    [InlineData("relative")]
    public void NavigateRejectsUnsafeSchemes(string address)
    {
        var sent = new List<WebViewCommand>();
        var session = new UiWebViewSession(new("https://example.invalid/"), () => { }, sent.Add);
        session.Update(UiWebViewState.Ready);
        Assert.Throws<ArgumentException>(() => session.NavigateAsync(new(address, UriKind.RelativeOrAbsolute)));
        Assert.Empty(sent);
    }

    /// <summary>Passive rebuilds retain the element and unmount only releases the borrowing attachment.</summary>
    [Fact]
    public void DeclarationRebuildAndUnmountNeverNavigateOrClose()
    {
        var sent = new List<WebViewCommand>();
        var session = new UiWebViewSession(new("https://example.invalid/"), () => { }, sent.Add);
        using (var surface = new UiSurface(WebViewCompose.WebView(session: session)))
        {
            UiElement element = surface.RootElement;
            surface.SetViewport(new(320, 200));
            surface.UpdateLayout();
            surface.Rebuild(WebViewCompose.WebView(session: session, automationName: "rebuilt"));
            surface.UpdateLayout();
            Assert.Same(element, surface.RootElement);
            Assert.Equal(UiRole.Group, surface.Root.Role);
            Assert.True(surface.Root.Focusable);
            Assert.True(surface.Root.ClipToBounds);
        }

        using var reattached = new UiSurface(WebViewCompose.WebView(session: session));
        Assert.Equal(UiWebViewState.Creating, session.State);
        Assert.Empty(sent);
    }

    /// <summary>The required borrowed session is validated before publication.</summary>
    [Fact]
    public void MissingSessionIsRejected() => Assert.Throws<ArgumentNullException>(() => new UiSurface(WebViewCompose.WebView()));
}
