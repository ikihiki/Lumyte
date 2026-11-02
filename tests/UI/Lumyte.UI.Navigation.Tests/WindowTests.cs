using Lumyte.UI.Application;
using Lumyte.UI.Application.Diagnostics;
using Lumyte.UI.Application.Headless;
using Lumyte.UI.Application.Navigation;
using Lumyte.UI.Application.Navigation.Diagnostics;
using Xunit;
using static Lumyte.UI.Application.Compose;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Navigation.Tests;

/// <summary>Checks independent navigation and teardown in declared hosted windows.</summary>
public sealed class WindowTests
{
    /// <summary>Checks simultaneous publications, independent Back and a fresh navigator after a keyed window is reopened.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task EachWindowOwnsItsHistoryAndActivationLifetimeAsync()
    {
        var provider = new PageProvider();
        UiPageKey<UiNoArgs> home = provider.Register("/", (_, _) => new(Panel()));
        UiPageKey<int> details = provider.Register<int>("/details", static value => value, (_, _) => new(Panel()));
        var main = new UiNavigationApplicationContent(provider, nav => nav.RequestNavigate(home, default));
        UiNavigationApplicationContent? child = null;
        UiApplicationContext? applicationContext = null;
        bool childOpen = true;
        int closed = 0;
        int opened = 0;
        UiApplicationBuilder builder = UiApplication.CreateBuilder().UseHeadless().UseDiagnostics(new() { Runtime = new() { MaximumTargets = 8 } }).UseNavigationDiagnostics().Configure(new() { FrameInterval = TimeSpan.FromMilliseconds(1) });
        builder.UseWindows(context =>
        {
            applicationContext = context;
            return [Window("main", "Main", content: _ => main), Window("child", "Child", content: _ => child = new(provider, nav => nav.RequestNavigate(home, default)), initialClientSize: new(200, 120), isOpen: childOpen, onClosed: _ => closed++, onOpened: _ => opened++)];
        });
        await using UiApplication app = builder.Build();
        Task running = app.RunAsync();
        await WaitAsync(app, () => app.State == UiApplicationState.Running && main.Navigation.CaptureSnapshot().Phase == UiNavigationPhase.Idle && child!.Navigation.CaptureSnapshot().Phase == UiNavigationPhase.Idle);
        Guid childInstance = child!.WindowContext.Id;
        Guid childNavigator = (await app.Dispatcher.InvokeAsync(child.Navigation.CaptureSnapshot)).NavigatorId;
        UiNavigationRequest first = await app.Dispatcher.InvokeAsync(() => main.Navigation.RequestNavigate(details, 1));
        UiNavigationRequest second = await app.Dispatcher.InvokeAsync(() => child.Navigation.RequestNavigate(details, 2));
        Assert.Equal(UiNavigationStatus.Committed, (await first.Completion).Status);
        Assert.Equal(UiNavigationStatus.Committed, (await second.Completion).Status);
        await WaitAsync(app, () => main.Navigation.CaptureSnapshot().Phase == UiNavigationPhase.Idle && child.Navigation.CaptureSnapshot().Phase == UiNavigationPhase.Idle);
        Assert.NotEqual((await app.Dispatcher.InvokeAsync(main.Navigation.CaptureSnapshot)).NavigatorId, childNavigator);
        UiNavigationRequest back = await app.Dispatcher.InvokeAsync(() => main.Navigation.RequestBack());
        await back.Completion;
        await WaitAsync(app, () => main.Navigation.CaptureSnapshot().Phase == UiNavigationPhase.Idle);
        Assert.Equal(0, (await app.Dispatcher.InvokeAsync(main.Navigation.CaptureSnapshot)).CurrentIndex);
        UiNavigationSnapshot childHistory = await app.Dispatcher.InvokeAsync(child.Navigation.CaptureSnapshot);
        Assert.Equal(1, childHistory.CurrentIndex);
        Assert.Equal(2, childHistory.Entries[1].Destination.GetArguments<int>());
        await app.Dispatcher.InvokeAsync(() =>
        {
            childOpen = false;
            applicationContext!.RequestRebuildWindows();
        });
        await WaitAsync(app, () => closed == 1);
        Assert.Equal(2, (await app.Dispatcher.InvokeAsync(main.Navigation.CaptureSnapshot)).Entries.Count);
        await app.Dispatcher.InvokeAsync(() =>
        {
            childOpen = true;
            applicationContext!.RequestRebuildWindows();
        });
        await WaitAsync(app, () => opened == 2 && child!.WindowContext.Id != childInstance && child.Navigation.CaptureSnapshot().Phase == UiNavigationPhase.Idle);
        Assert.NotEqual(childNavigator, (await app.Dispatcher.InvokeAsync(child.Navigation.CaptureSnapshot)).NavigatorId);
        Assert.Single((await app.Dispatcher.InvokeAsync(child.Navigation.CaptureSnapshot)).Entries);
        await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await running;
    }

    private static async Task WaitAsync(UiApplication app, Func<bool> predicate)
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

        while (!await app.Dispatcher.InvokeAsync(predicate, timeout.Token))
        {
            await Task.Delay(1, timeout.Token);
        }
    }
}
