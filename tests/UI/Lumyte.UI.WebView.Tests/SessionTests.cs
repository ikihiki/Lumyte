using Xunit;

namespace Lumyte.UI.WebView.Tests;

/// <summary>Exercises owner commands independently from any installed browser runtime.</summary>
public sealed class SessionTests
{
    /// <summary>Creation buffers a bounded sequence and flushes it only once.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task CreatingCommandsAreBoundedAndOrderedAsync()
    {
        var commands = new List<WebViewCommand>();
        var session = new UiWebViewSession(new("https://example.com/"), () => { }, commands.Add);
        for (int i = 0; i < 16; i++)
        {
            await session.NavigateAsync(new($"https://example.com/{i}"));
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ReloadAsync().AsTask());
        Assert.Empty(commands);
        session.Update(UiWebViewState.Ready);
        session.Update(UiWebViewState.Ready);
        Assert.Equal(16, commands.Count);
        Assert.Equal(Enumerable.Range(1, 16).Select(value => (long)value), commands.Select(value => value.Sequence));
    }

    /// <summary>Unaccepted cancellation never reaches the browser.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task CancelledCommandIsNotAcceptedAsync()
    {
        var commands = new List<WebViewCommand>();
        var session = new UiWebViewSession(new("https://example.com/"), () => { }, commands.Add);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.ReloadAsync(new(true)).AsTask());
        session.Update(UiWebViewState.Ready);
        Assert.Empty(commands);
    }

    /// <summary>Close while creating discards queued work and rejects late readiness.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task CloseDuringCreationDoesNotResurrectAsync()
    {
        var commands = new List<WebViewCommand>();
        var session = new UiWebViewSession(new("https://example.com/"), () => { }, commands.Add);
        await session.ReloadAsync();
        session.Update(UiWebViewState.Closing);
        session.Update(UiWebViewState.Ready);
        Assert.Equal(UiWebViewState.Closing, session.State);
        Assert.Empty(commands);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.GoBackAsync().AsTask());
    }

    /// <summary>Commands cannot use external or executable schemes.</summary>
    /// <param name="uri">The rejected address.</param>
    [Theory]
    [InlineData("file:///tmp/private")]
    [InlineData("javascript:alert(1)")]
    [InlineData("mailto:user@example.com")]
    [InlineData("relative")]
    public void NonHttpAddressIsDenied(string uri) => Assert.Throws<ArgumentException>(() => new UiWebViewSession(new(uri, UriKind.RelativeOrAbsolute), () => { }, _ => { }));

    /// <summary>One session cannot be borrowed by two elements.</summary>
    [Fact]
    public void AttachmentIsExclusive()
    {
        var session = new UiWebViewSession(new("https://example.com/"), () => { }, _ => { });
        object first = new();
        object second = new();
        session.Attach(first);
        Assert.Throws<InvalidOperationException>(() => session.Attach(second));
        session.Detach(second);
        Assert.Throws<InvalidOperationException>(() => session.Attach(second));
        session.Detach(first);
        session.Attach(second);
    }
}
