using System.Numerics;
using Lumyte.Input.Abstractions;
using Xunit;
using WebCompose = Lumyte.UI.WebView.Compose;

namespace Lumyte.UI.WebView.Tests;

/// <summary>Checks view requests against an explicitly fake host.</summary>
public sealed class ViewHostTests
{
    /// <summary>Checks that physical input and text remain separate across rebuilds.</summary>
    [Fact]
    public void FakeHostReceivesBodyLocalPointerFocusAndTextWithoutNavigation()
    {
        var commands = new List<WebViewCommand>();
        var host = new FakeHost();
        var session = new UiWebViewSession(new("https://example.invalid/"), () => { }, commands.Add) { Host = host };
        using var surface = new UiSurface(WebCompose.WebView(session: session));
        surface.SetViewport(new(100, 80));
        surface.UpdateLayout();
        Assert.Equal(new Vector2(100, 80), host.Size);
        Assert.True(surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(10, 20))));
        Assert.True(host.Focused);
        Assert.Equal(new Vector2(10, 20), host.Point);
        Assert.True(surface.Dispatch(new UiCommittedTextInput("日本語")));
        Assert.IsType<UiCommittedTextInput>(host.LastInput);
        Assert.True(surface.Dispatch(new UiKeyInput(Key.Tab, true)));
        Assert.IsType<UiKeyInput>(host.LastInput);
        surface.Rebuild(WebCompose.WebView(session: session));
        surface.UpdateLayout();
        Assert.Empty(commands);
        surface.Dispose();
        Assert.True(host.Detached);
        Assert.Equal(UiWebViewState.Creating, session.State);
    }

    private sealed class FakeHost : IWebViewHost
    {
        internal Vector2 Size { get; private set; }

        internal Vector2? Point { get; private set; }

        internal UiInput? LastInput { get; private set; }

        internal bool Focused { get; private set; }

        internal bool Detached { get; private set; }

        public void ValidateSurface(UiSurface surface) => Assert.NotNull(surface);

        public void Viewport(Vector2 logicalSize, bool visible) => Size = logicalSize;

        public void Input(UiInput input, Vector2? localPosition)
        {
            LastInput = input;
            Point = localPosition;
        }

        public void Focus(bool focused) => Focused = focused;

        public void Detach() => Detached = true;
    }
}
