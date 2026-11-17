using System.Numerics;
using System.Reflection;
using Lumyte.Signals;
using Lumyte.UI.Application;
using Lumyte.UI.Application.Headless;
using Lumyte.UI.Application.WebView.Cef;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Lumyte.UI.WebView.UiWebViewHooks;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.WebView.Tests;

/// <summary>Generated function hooks and the real per-window integration, without loading native CEF.</summary>
public sealed class UseWebViewTests
{
    /// <summary>Hooks reject callback/outside-function use and explain missing backend configuration.</summary>
    [Fact]
    public void FunctionAndProviderBoundariesAreExplicit()
    {
        Assert.Contains("composable function", Assert.Throws<InvalidOperationException>(() => UseWebView("help", new("https://example.invalid/"))).Message);
        var probe = new WebViewFunctionUi.Probe();
        Assert.Empty(probe.Sessions);
        Widget passive = WebViewFunctionUi.Root(probe);
        Assert.Empty(probe.Sessions);
        Assert.Contains("UseCefWebViews", Assert.Throws<InvalidOperationException>(() => new UiSurface(passive)).Message);
    }

    /// <summary>Standalone engine-free providers preserve borrowed sessions, layout, clipping and native input.</summary>
    [Fact]
    public void NestedFunctionComposesWithNativeUiAndRetainsBorrowedSession()
    {
        var probe = new WebViewFunctionUi.Probe();
        var provider = new Sessions();
        Widget Tree() => ProvideWebViewSessions(provider, WebViewFunctionUi.Root(probe));
        using var surface = new UiSurface(Tree());
        surface.SetViewport(new(220, 170));
        surface.UpdateLayout();
        UiWebViewSession session = Assert.Single(probe.Sessions);
        object token = Assert.Single(probe.Tokens);
        UiElement browser = Elements(surface.RootElement).Single(element => element.Widget.AutomationName == "help-browser");
        Assert.Equal(new Vector2(180, 110), browser.Size);
        Assert.True(Origin(browser).Y > 24);
        Assert.Contains(Elements(surface.RootElement), element => element.Widget is Lumyte.UI.Media.Compose.Definitions.Text);
        Assert.True(surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(5, 25))));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(5, 25)));
        Assert.Equal(1, probe.Clicks);
        Assert.False(surface.Dispatch(new UiPointerInput(2, UiPointerPhase.Down, new(5, 185))));
        Delegate[] subscribers = Subscribers(session);
        Assert.Single(subscribers);
        for (int index = 0; index < 3; index++)
        {
            surface.Rebuild(Tree());
            surface.UpdateLayout();
            probe.Revision.Value++;
            surface.UpdateLayout();
        }

        Assert.All(probe.Sessions, item => Assert.Same(session, item));
        Assert.All(probe.Tokens, item => Assert.Same(token, item));
        Assert.Equal(subscribers, Subscribers(session));
        Assert.Equal(1, provider.Created);
        Assert.Empty(provider.Commands);
        Assert.Null(typeof(UiWebViewSession).GetField("StateChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session));
        probe.Click!();
        Assert.Equal(2, probe.Clicks);
        Assert.Throws<InvalidOperationException>(() => new UiSurface(ProvideWebViewSessions(provider, WebViewFunctionUi.HelpPane(probe))));
        probe.ShowView = false;
        surface.Rebuild(Tree());
        surface.UpdateLayout();
        Assert.Empty(Subscribers(session));
        Assert.Equal(UiWebViewState.Creating, session.State);
        probe.ShowView = true;
        surface.Rebuild(Tree());
        surface.UpdateLayout();
        Assert.Same(session, probe.Sessions[^1]);
        Assert.Single(Subscribers(session));
    }

    /// <summary>Keys and initial addresses are validated on every build, including retained functions.</summary>
    /// <param name="key">The invalid key.</param>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void EmptyKeysAreRejected(string key)
    {
        var probe = new WebViewFunctionUi.Probe { SessionKey = key };
        Assert.Throws<ArgumentException>(() => new UiSurface(ProvideWebViewSessions(new Sessions(), WebViewFunctionUi.HelpPane(probe))));
    }

    /// <summary>A provider cannot turn unsupported addresses into implicit browser requests.</summary>
    /// <param name="address">The unsupported address.</param>
    [Theory]
    [InlineData("file:///private.html")]
    [InlineData("relative/path")]
    public void UnsupportedInitialAddressesAreRejected(string address)
    {
        var probe = new WebViewFunctionUi.Probe { InitialUri = new Uri(address, UriKind.RelativeOrAbsolute) };
        Assert.Throws<ArgumentException>(() => new UiSurface(ProvideWebViewSessions(new Sessions(), WebViewFunctionUi.HelpPane(probe))));
    }

    /// <summary>URI changes cannot silently renavigate an existing key.</summary>
    [Fact]
    public void ChangedInitialUriIsRejectedWithoutCommands()
    {
        var probe = new WebViewFunctionUi.Probe();
        var provider = new Sessions();
        using var surface = new UiSurface(ProvideWebViewSessions(provider, WebViewFunctionUi.HelpPane(probe)));
        probe.InitialUri = new("https://example.invalid/other");
        surface.Rebuild(ProvideWebViewSessions(provider, WebViewFunctionUi.HelpPane(probe)));
        Assert.Throws<ArgumentException>(surface.UpdateTree);
        Assert.Equal(1, provider.Created);
        Assert.Empty(provider.Commands);
    }

    /// <summary>Automatic window context survives bare and graphics root rebuilds, including root-function signals.</summary>
    /// <param name="nested">Whether the hook is nested beneath another function and native layout.</param>
    /// <returns>The verification.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealWindowProviderSurvivesRebuildAndMissingHelperAsync(bool nested)
    {
        var probe = new WebViewFunctionUi.Probe();
        UiApplicationWindowContext? window = null;
        Widget Root() => nested ? WebViewFunctionUi.Root(probe) : WebViewFunctionUi.HelpPane(probe);
        UiApplicationBuilder builder = Builder();
        builder.UseRoot(context =>
        {
            window = context;
            return Root();
        });
        await using UiApplication app = builder.Build();
        Task running = app.RunAsync();
        await WaitAsync(app, () => app.State == UiApplicationState.Running && probe.Sessions.Count > 0 && probe.Sessions[0].State == UiWebViewState.Faulted);
        await app.Dispatcher.InvokeAsync(() =>
        {
            UiWebViewSession browser = probe.Sessions[0];
            Assert.Equal("cef-helper-unavailable", browser.FailureCode);
            Assert.False(browser.NavigationCommandsAvailable);
            Delegate[] subscribers = Subscribers(browser);
            UiSurface surface = window!.View.CurrentSurface!;
            object token = probe.Tokens[0];

            // Invoke the actual static-content graphics rebuild boundary (no device-loss claim).
            Type contentType = typeof(UiApplication).Assembly.GetType("Lumyte.UI.Application.UiRootApplicationContent")!;
            object graphicsContent = Activator.CreateInstance(contentType, (Func<UiApplicationWindowContext, Widget>)(_ => Root()))!;
            var graphicsRebuild = (ValueTask)contentType.GetMethod("RebuildGraphicsAsync")!.Invoke(graphicsContent, [window])!;
            Assert.True(graphicsRebuild.IsCompletedSuccessfully);
            surface.UpdateLayout();
            for (int index = 0; index < 3; index++)
            {
                surface.Rebuild(Root());
                surface.UpdateLayout();
                probe.Revision.Value++;
                surface.UpdateLayout();
            }

            Assert.All(probe.Sessions, item => Assert.Same(browser, item));
            Assert.All(probe.Tokens, item => Assert.Same(token, item));
            Assert.Equal(subscribers, Subscribers(browser));
            Assert.Null(typeof(UiWebViewSession).GetField("StateChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(browser));
            Assert.Same(browser, window.GetWebViewSession("help", probe.InitialUri));
            Assert.Throws<ArgumentException>(() => window.GetWebViewSession("help", new("https://example.invalid/changed")));
            Assert.Equal("cef-session-budget-exceeded", Assert.Throws<InvalidOperationException>(() => window.GetWebViewSession("second", probe.InitialUri)).Message);
            probe.Click!();
            surface.UpdateLayout();
            Assert.Same(browser, probe.Sessions[^1]);
            Assert.Equal(1, probe.Clicks);
        });
        await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await running.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(UiWebViewState.Closed, probe.Sessions[0].State);
        Assert.Empty(Subscribers(probe.Sessions[0]));
    }

    /// <summary>The same logical window key gets a new GUID and session after closure; captured providers cannot escape ownership.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task ReopenedWindowDoesNotReuseSessionOrCapturedProviderAsync()
    {
        var open = Signal.Create(true);
        var probe = new WebViewFunctionUi.Probe();
        UiApplicationWindowContext? child = null;
        Widget? captured = null;
        int opened = 0;
        int closed = 0;
        UiApplicationBuilder builder = Builder();
        builder.UseWindows(_ => [
            Lumyte.UI.Application.Compose.Window("main", "Main", root: _ => Lumyte.UI.Compose.Panel()),
            Lumyte.UI.Application.Compose.Window(
                "child",
                "Child",
                root: context =>
            {
                child = context;
                captured = context.Services.GetRequiredService<CefWebViewParticipant>().ProvideRootContext(context, WebViewFunctionUi.HelpPane(new WebViewFunctionUi.Probe { ShowView = false }));
                return WebViewFunctionUi.Root(probe);
            },
                isOpen: open,
                onOpened: _ => opened++,
                onClosed: _ => closed++)]);
        await using UiApplication app = builder.Build();
        Task running = app.RunAsync();
        await WaitAsync(app, () => opened == 1);
        Guid firstId = child!.Id;
        UiWebViewSession first = probe.Sessions[0];
        Widget stale = captured!;
        await Task.Run(() => Assert.Throws<InvalidOperationException>(() => new UiSurface(stale)));
        open.Value = false;
        await WaitAsync(app, () => closed == 1);
        await app.Dispatcher.InvokeAsync(() => Assert.Throws<ObjectDisposedException>(() => new UiSurface(stale)));
        open.Value = true;
        await WaitAsync(app, () => opened == 2);
        await app.Dispatcher.InvokeAsync(() =>
        {
            Assert.NotEqual(firstId, child.Id);
            Assert.NotSame(first, probe.Sessions[^1]);
            Assert.Equal(UiWebViewState.Closed, first.State);
            Assert.Throws<ObjectDisposedException>(() => new UiSurface(stale));
        });
        await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await running.WaitAsync(TimeSpan.FromSeconds(10));
    }

    /// <summary>Separate applications never reuse the same keyed session through a process-global provider.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task SeparateApplicationsNeverShareSessionsAsync()
    {
        UiWebViewSession? previous = null;
        Guid previousWindow = default;
        for (int index = 0; index < 2; index++)
        {
            var probe = new WebViewFunctionUi.Probe();
            UiApplicationWindowContext? window = null;
            UiApplicationBuilder builder = Builder().UseRoot(context =>
            {
                window = context;
                return WebViewFunctionUi.Root(probe);
            });
            await using UiApplication app = builder.Build();
            Task running = app.RunAsync();
            await WaitAsync(app, () => app.State == UiApplicationState.Running && probe.Sessions.Count > 0);
            await app.Dispatcher.InvokeAsync(() =>
            {
                Assert.NotEqual(previousWindow, window!.Id);
                Assert.NotSame(previous, probe.Sessions[0]);
                previousWindow = window.Id;
                previous = probe.Sessions[0];
            });
            await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await running.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private static UiApplicationBuilder Builder()
    {
        UiApplicationBuilder builder = UiApplication.CreateBuilder().UseHeadless().Configure(new() { FrameInterval = TimeSpan.FromMilliseconds(1) });

        // Engine-free real-window fixture; product still rejects a non-desktop driver.
        typeof(UiApplicationBuilder).GetProperty("IntegrationDriverKind", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(builder, "desktop");
        return builder.UseCefWebViews(new());
    }

    private static IEnumerable<UiElement> Elements(UiElement element) => new[] { element }.Concat(element.Children.SelectMany(Elements));

    private static Vector2 Origin(UiElement element) => new Vector2(element.Bounds.X, element.Bounds.Y) + (element.Parent is null ? Vector2.Zero : Origin(element.Parent));

    private static Delegate[] Subscribers(UiWebViewSession session) =>
        ((Delegate?)typeof(UiWebViewSession).GetField("PixelsChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session))?.GetInvocationList() ?? [];

    private static async Task WaitAsync(UiApplication app, Func<bool> ready)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (app.State == UiApplicationState.Starting)
        {
            if (app.Completion.IsCompleted)
            {
                await app.Completion;
            }

            await Task.Delay(1, timeout.Token);
        }

        while (!await app.Dispatcher.InvokeAsync(ready, timeout.Token))
        {
            Assert.Null(app.Failure);
            await Task.Delay(1, timeout.Token);
        }
    }

    private sealed class Sessions : IUiWebViewSessions
    {
        private readonly Dictionary<string, UiWebViewSession> _sessions = [];

        internal int Created { get; private set; }

        internal List<WebViewCommand> Commands { get; } = [];

        public UiWebViewSession GetSession(string key, Uri initialUri)
        {
            if (_sessions.TryGetValue(key, out UiWebViewSession? session))
            {
                if (session.InitialUri != initialUri)
                {
                    throw new ArgumentException("immutable initial URI", nameof(initialUri));
                }

                return session;
            }

            Created++;
            session = new(initialUri, () => { }, Commands.Add);
            _sessions.Add(key, session);
            return session;
        }
    }
}
