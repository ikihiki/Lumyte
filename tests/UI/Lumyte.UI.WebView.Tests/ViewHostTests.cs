using System.Numerics;
using Lumyte.Input.Abstractions;
using Lumyte.UI.Application;
using Lumyte.UI.Application.Headless;
using Lumyte.UI.Automation;
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

    /// <summary>Both physical transitions reach the host; committed text is a distinct event.</summary>
    [Fact]
    public void FocusedSurfaceRoutesBothPhysicalTransitionsSeparatelyFromText()
    {
        var host = new FakeHost();
        var session = new UiWebViewSession(new("https://example.invalid/"), () => { }, _ => { }) { Host = host };
        using var surface = new UiSurface(WebCompose.WebView(session: session));
        surface.SetViewport(new(100, 80));
        surface.UpdateLayout();
        Assert.True(surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(10, 20))));
        Assert.True(surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(10, 20))));
        Assert.IsType<WebViewElement>(surface.FocusedElement);
        host.Inputs.Clear();
        Assert.True(surface.Dispatch(new UiKeyInput(Key.A, true)));
        Assert.True(surface.Dispatch(new UiCommittedTextInput("a")));
        Assert.True(surface.Dispatch(new UiKeyInput(Key.A, false)));
        Assert.Collection(
            host.Inputs,
            input => Assert.Equal(new UiKeyInput(Key.A, true), input),
            input => Assert.Equal(new UiCommittedTextInput("a"), input),
            input => Assert.Equal(new UiKeyInput(Key.A, false), input));
    }

    /// <summary>The public input lease delivers both raw transitions when its keyboard root is eligible.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task ActiveApplicationLeaseRoutesBothPhysicalTransitionsAsync()
    {
        var host = new FakeHost();
        var session = new UiWebViewSession(new("https://example.invalid/"), () => { }, _ => { }) { Host = host };
        UiApplicationWindowContext? window = null;
        UiApplicationBuilder builder = UiApplication.CreateBuilder().UseHeadless().Configure(new() { FrameInterval = TimeSpan.FromMilliseconds(1) });
        builder.UseRoot(context =>
        {
            window = context;
            return WebCompose.WebView(session: session);
        });
        await using UiApplication app = builder.Build();
        Task running = app.RunAsync();
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            while (app.State != UiApplicationState.Running)
            {
                await Task.Delay(1, deadline.Token);
            }

            await using IUiAutomationInputLease lease = await window!.Automation.AcquireInputAsync(deadline.Token);
            await window.Automation.RunOnUiThreadAsync(
            surface =>
            {
                Assert.True(((IUiAutomationRoutingHost)window.Automation).CanReceiveKeys(surface));
                Assert.Equal(window.Window, window.Windows.GetKeyboardTarget(window.Window));
                Assert.True(lease.Dispatch(surface, new UiPointerInput(lease.PointerId, UiPointerPhase.Down, new(10, 20))));
                Assert.True(lease.Dispatch(surface, new UiPointerInput(lease.PointerId, UiPointerPhase.Up, new(10, 20))));
                Assert.IsType<WebViewElement>(surface.FocusedElement);
                host.Inputs.Clear();
                Assert.True(lease.Dispatch(surface, new UiKeyInput(Key.A, true)));
                Assert.True(surface.Dispatch(new UiCommittedTextInput("a")));
                Assert.True(lease.Dispatch(surface, new UiKeyInput(Key.A, false)));
                Assert.Collection(
                    host.Inputs,
                    input => Assert.Equal(new UiKeyInput(Key.A, true), input),
                    input => Assert.Equal(new UiCommittedTextInput("a"), input),
                    input => Assert.Equal(new UiKeyInput(Key.A, false), input));
                return true;
            },
            deadline.Token);
        }
        finally
        {
            await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(8));
            await running.WaitAsync(TimeSpan.FromSeconds(8));
        }
    }

    private sealed class FakeHost : IWebViewHost
    {
        internal List<UiInput> Inputs { get; } = [];

        internal Vector2 Size { get; private set; }

        internal Vector2? Point { get; private set; }

        internal UiInput? LastInput { get; private set; }

        internal bool Focused { get; private set; }

        internal bool Detached { get; private set; }

        public void ValidateSurface(UiSurface surface) => Assert.NotNull(surface);

        public void Viewport(Vector2 logicalSize, bool visible) => Size = logicalSize;

        public void Input(UiInput input, Vector2? localPosition)
        {
            Inputs.Add(input);
            LastInput = input;
            Point = localPosition;
        }

        public void Focus(bool focused) => Focused = focused;

        public void Detach() => Detached = true;
    }
}
