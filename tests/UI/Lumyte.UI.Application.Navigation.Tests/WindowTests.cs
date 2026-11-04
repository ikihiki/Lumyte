using Lumyte.UI.Application.Headless;
using Lumyte.UI.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Lumyte.UI.Application.Compose;
using static Lumyte.UI.Application.Navigation.UiPageWindows;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Application.Navigation.Tests;

/// <summary>Checks DI page factories create independent navigation controllers for each window instance.</summary>
public sealed class WindowTests
{
    /// <summary>Checks typed argument copying, per-window scopes and same-key declaration rebuilds.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task WindowFactoriesCreateIndependentScopesAndPreserveExistingInstancesAsync()
    {
        var probe = new WindowProbe();
        var arguments = new MutableArguments { Value = 1 };
        UiApplicationContext? applicationContext = null;
        bool childOpen = true;
        int declarations = 0;
        UiApplicationBuilder builder = UiApplication.CreateBuilder().UseHeadless().Configure(new() { FrameInterval = TimeSpan.FromMilliseconds(1) });
        builder.Services.AddSingleton(probe);
        UiPageKey<MutableArguments> page = builder.MapGroup("/documents").MapPage<DocumentPage, MutableArguments>("/editor", static value => new() { Value = value.Value });
        builder.UseWindows(context =>
        {
            applicationContext = context;
            declarations++;
            return [
                Window("main", "Main", content: Page(page, arguments)),
                Window("child", "Child", owner: "main", isOpen: childOpen, content: Page(page, arguments))];
        });
        await using UiApplication app = builder.Build();
        Assert.Empty(probe.Contexts);
        Task running = app.RunAsync();
        await InlineTests.WaitAsync(app, () => probe.Contexts.Count == 2 && probe.Contexts.All(context => context.Activation.Navigation.CaptureSnapshot().Phase == UiNavigationPhase.Idle));
        Assert.NotSame(probe.Contexts[0], probe.Contexts[1]);
        Assert.NotEqual(probe.Contexts[0].Activation.Window, probe.Contexts[1].Activation.Window);
        Assert.NotSame(probe.Contexts[0].Activation.Navigation, probe.Contexts[1].Activation.Navigation);
        await app.Dispatcher.InvokeAsync(() =>
        {
            arguments.Value = 9;
            MutableArguments copy = probe.Contexts[0].Activation.Destination.GetArguments<MutableArguments>();
            Assert.Equal(1, copy.Value);
            copy.Value = 123;
            Assert.Equal(1, probe.Contexts[0].Activation.Destination.GetArguments<MutableArguments>().Value);
            applicationContext!.RequestRebuildWindows();
        });
        await InlineTests.WaitAsync(app, () => declarations >= 2);
        await app.Dispatcher.InvokeAsync(() =>
        {
            Assert.Equal(2, probe.Contexts.Count);
            childOpen = false;
            applicationContext!.RequestRebuildWindows();
        });
        await InlineTests.WaitAsync(app, () => probe.Disposals == 1);
        await app.Dispatcher.InvokeAsync(() =>
        {
            childOpen = true;
            applicationContext!.RequestRebuildWindows();
        });
        await InlineTests.WaitAsync(app, () => probe.Contexts.Count == 3 && probe.Contexts[2].Activation.Navigation.CaptureSnapshot().Phase == UiNavigationPhase.Idle);
        Assert.Equal(9, probe.Contexts[2].Activation.Destination.GetArguments<MutableArguments>().Value);
        Assert.Equal(1, probe.Contexts[0].Activation.Destination.GetArguments<MutableArguments>().Value);
        await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await running;
        Assert.Equal(3, probe.Disposals);
    }

    private sealed class WindowProbe
    {
        internal List<UiPageContext> Contexts { get; } = [];

        internal int Disposals { get; set; }
    }

    private sealed class MutableArguments
    {
        internal int Value { get; set; }
    }

    private sealed class DocumentPage(UiPageContext context, WindowProbe probe) : IUiPage, IAsyncDisposable
    {
        public Widget Build(UiPageContext current)
        {
            Assert.Same(context, current);
            probe.Contexts.Add(current);
            return Panel();
        }

        public ValueTask DisposeAsync()
        {
            probe.Disposals++;
            return ValueTask.CompletedTask;
        }
    }
}
