using Lumyte.Signals;
using Lumyte.UI.Application.Diagnostics;
using Lumyte.UI.Application.Headless;
using Lumyte.UI.Automation;
using Xunit;
using static Lumyte.UI.Application.Compose;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Application.Tests;

/// <summary>Exercises actual keyed declarations, routed input and hosted cleanup.</summary>
public sealed class WindowTests
{
    /// <summary>Checks closed declarations remain observed and title changes retain an existing content lifetime.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task SignalsOpenAndCloseWindowsWithoutExplicitRebuildAsync()
    {
        var open = Signal.Create(false);
        var title = Signal.Create("First");
        var include = Signal.Create(true);
        int created = 0;
        int closed = 0;
        int opened = 0;
        UiApplicationWindowContext? child = null;
        UiApplicationBuilder builder = Builder();
        builder.UseWindows(_ =>
        {
            UiWindowDeclaration main = Window("main", "Main", root: _ => Panel());
            if (!include.Value)
            {
                return [main];
            }

            return [main, Window(
                "child",
                title,
                root: context =>
                {
                    created++;
                    child = context;
                    return Panel();
                },
                isOpen: open,
                onOpened: _ => opened++,
                onClosed: _ => closed++)];
        });
        await using UiApplication app = builder.Build();
        Task run = app.RunAsync();
        await WaitAsync(app, () => app.State == UiApplicationState.Running);
        Assert.Equal(0, created);
        open.Value = true;
        await WaitAsync(app, () => opened == 1);
        Guid identity = child!.Id;
        title.Value = "Second";
        var chrome = new UiAutomationPage(child.Chrome!.Header);
        await chrome.Expect(chrome.GetByRole(UiRole.Button, "Second")).ToBeVisibleAsync();
        await app.Dispatcher.InvokeAsync(() =>
        {
            Assert.Equal(1, created);
            Assert.Equal(identity, child.Id);
        });
        open.Value = false;
        await WaitAsync(app, () => closed == 1);
        open.Value = true;
        await WaitAsync(app, () => opened == 2);
        Assert.NotEqual(identity, child.Id);
        include.Value = false;
        await WaitAsync(app, () => closed == 2);
        include.Value = true;
        await WaitAsync(app, () => opened == 3);
        await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await run;
        open.Value = false;
        Assert.Equal(3, closed);
    }

    /// <summary>Checks a model change between snapshot construction and observer attachment cannot be lost.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task WindowAttachmentObservesChangesAfterTheInitialSnapshotAsync()
    {
        var source = new SnapshotRaceSignal();
        int opened = 0;
        UiApplicationBuilder builder = Builder();
        builder.UseWindows(_ => [Window("main", "Main", root: _ => Panel()), Window("child", "Child", root: _ => Panel(), isOpen: UiArgument<bool>.FromSignal(source), onOpened: _ => opened++)]);
        await using UiApplication app = builder.Build();
        Task run = app.RunAsync();
        await WaitAsync(app, () => opened == 1);
        await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await run;
    }

    /// <summary>Checks reactive failure stops frame work and still completes normal resource cleanup.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task SignalFailureStopsTheApplicationAndDrainsAsync()
    {
        var fail = Signal.Create(false);
        Computed<float?> width = Signal.Computed<float?>(() => fail.Value ? throw new ArithmeticException("source") : 20);
        UiApplication app = Builder().UseRoot(_ => Rectangle(width: width)).Build();
        Task running = app.RunAsync();
        await WaitAsync(app, () => app.State == UiApplicationState.Running);
        fail.Value = true;
        UiApplicationException error = await Assert.ThrowsAsync<UiApplicationException>(() => running.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("state-update-failed", error.Code);
        Assert.Equal(UiApplicationState.Failed, app.State);
        await Assert.ThrowsAsync<UiApplicationException>(() => app.DisposeAsync().AsTask());
    }

    /// <summary>Checks a hosted window retains content while the declaration key remains and recreates after close.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task HostedKeysRetainStateAndReopenWithNewIdentityAsync()
    {
        UiApplicationContext? application = null;
        UiApplicationWindowContext? child = null;
        bool open = true;
        int factories = 0;
        int closed = 0;
        UiApplicationBuilder builder = Builder();
        builder.UseWindows(context =>
        {
            application = context;
            return [Window("main", "Main", root: _ => Panel()), Window(
                "child",
                "Child",
                root: value =>
            {
                factories++;
                child = value;
                return Button(width: 40, height: 24, testId: "body");
            },
                isOpen: open,
                initialClientSize: new(200, 120),
                onClosed: _ => closed++)];
        });
        await using UiApplication app = builder.Build();
        Task run = app.RunAsync();
        await WaitAsync(app, () => app.State == UiApplicationState.Running);
        Guid first = child!.Id;
        await app.Dispatcher.InvokeAsync(application!.RequestRebuildWindows);
        await child.Automation.NextFrameAsync();
        await app.Dispatcher.InvokeAsync(() => Assert.Equal(1, factories));
        await app.Dispatcher.InvokeAsync(() =>
        {
            open = false;
            application.RequestRebuildWindows();
        });
        await WaitAsync(app, () => closed == 1);
        await app.Dispatcher.InvokeAsync(() =>
        {
            open = true;
            application.RequestRebuildWindows();
        });
        await WaitAsync(app, () => factories == 2);
        Assert.NotEqual(first, child.Id);
        await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await run;
        Assert.Equal(2, closed);
    }

    /// <summary>Checks native-required failure is isolated and retries only after false then true.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task UnsupportedAdditionalNativeWindowDoesNotStopPrimaryAsync()
    {
        UiApplicationContext? context = null;
        int failures = 0;
        UiApplicationBuilder builder = Builder();
        builder.UseWindows(value =>
        {
            context = value;
            return [Window("main", "Main", root: _ => Panel()), Window("native", "Native", root: _ => Panel(), presentation: WindowPresentation.NativeRequired, onClosed: result =>
            {
                Assert.Equal("native-unavailable", result.Code);
                failures++;
            })];
        });
        await using UiApplication app = builder.Build();
        Task run = app.RunAsync();
        await WaitAsync(app, () => app.State == UiApplicationState.Running);
        await app.Dispatcher.InvokeAsync(context!.RequestRebuildWindows);
        await Task.Delay(30);
        await app.Dispatcher.InvokeAsync(() => Assert.Equal(1, failures));
        await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await run;
    }

    /// <summary>Checks invalid lists leave the live primary and window state intact.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task InvalidRebuildPreservesCurrentWindowsAsync()
    {
        UiApplicationContext? context = null;
        UiApplicationWindowContext? primary = null;
        bool duplicate = false;
        UiApplicationBuilder builder = Builder();
        builder.UseWindows(value =>
        {
            context = value;
            UiWindowDeclaration main = Window(
                "main",
                "Main",
                root: window =>
                {
                    primary = window;
                    return Panel();
                });
            return duplicate ? [main, main] : [main];
        });
        await using UiApplication app = builder.Build();
        Task run = app.RunAsync();
        await WaitAsync(app, () => app.State == UiApplicationState.Running);
        Guid id = primary!.Id;
        await app.Dispatcher.InvokeAsync(() =>
        {
            duplicate = true;
            context!.RequestRebuildWindows();
        });
        await WaitAsync(app, () => app.LastWindowUpdateFailure is not null);
        Assert.Equal(UiApplicationState.Running, app.State);
        Assert.Equal(id, primary.Id);
        await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await run;
    }

    /// <summary>Checks hosted close chrome uses routed automation and a model update.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task HostedCloseButtonUpdatesModelAndDrainsAsync()
    {
        UiApplicationWindowContext? child = null;
        UiApplicationContext? context = null;
        bool open = true;
        int closed = 0;
        Exception? creationError = null;
        UiApplicationBuilder builder = Builder();
        builder.UseWindows(value =>
        {
            context = value;
            UiWindowDeclaration main = Window("main", "Main", root: _ => Panel());
            UiWindowDeclaration additional = Window(
                "child",
                "Child",
                root: window =>
                {
                    child = window;
                    return Panel();
                },
                isOpen: open,
                initialClientSize: new(200, 100),
                onCloseRequested: _ =>
                {
                    open = false;
                    context.RequestRebuildWindows();
                },
                onClosed: result =>
                {
                    creationError = result.Error;
                    closed++;
                });
            return [main, additional];
        });
        await using UiApplication app = builder.Build();
        Task run = app.RunAsync();
        await WaitAsync(app, () => app.State == UiApplicationState.Running);
        Assert.Null(creationError);
        var page = new UiAutomationPage(child!.Chrome!.Header);
        await page.GetByTestId("window-close").ClickAsync();
        await WaitAsync(app, () => closed == 1);
        await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await run;
    }

    /// <summary>Checks diagnostics creates and drains one scope without closing windows when target capacity is exhausted.</summary>
    /// <param name="capacity">The target budget.</param>
    /// <returns>The verification.</returns>
    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    public async Task DiagnosticsTargetsShareTheOwnerAndStopCleanlyAsync(int capacity)
    {
        int opened = 0;
        UiApplicationBuilder builder = Builder().UseDiagnostics(new() { Runtime = new() { MaximumTargets = capacity } });
        builder.UseWindows(_ => [Window("main", "Main", root: _ => Panel()), Window("child", "Child", root: _ => Panel(), initialClientSize: new(100, 80), onOpened: _ => opened++)]);
        await using UiApplication app = builder.Build();
        Task run = app.RunAsync();
        await WaitAsync(app, () => app.State == UiApplicationState.Running);
        Assert.Equal(1, opened);
        await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await run;
        Assert.Equal(UiApplicationState.Stopped, app.State);
    }

    private static UiApplicationBuilder Builder() => UiApplication.CreateBuilder().Configure(new() { FrameInterval = TimeSpan.FromMilliseconds(1) }).UseHeadless();

    private static async Task WaitAsync(UiApplication app, Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (app.State == UiApplicationState.Starting)
        {
            if (app.Completion.IsCompleted)
            {
                await app.Completion;
            }

            await Task.Delay(1, timeout.Token);
        }

        while (!await app.Dispatcher.InvokeAsync(condition, timeout.Token))
        {
            if (app.Completion.IsCompleted)
            {
                await app.Completion;
            }

            await Task.Delay(1, timeout.Token);
        }
    }

    private sealed class SnapshotRaceSignal : IReadSignal<bool>
    {
        private readonly Signal<bool> _value = Signal.Create(false);
        private int _peeks;

        public bool Value => _value.Value;

        public bool Peek()
        {
            bool value = _value.Peek();
            if (++_peeks == 2)
            {
                _value.Value = true;
            }

            return value;
        }
    }
}
