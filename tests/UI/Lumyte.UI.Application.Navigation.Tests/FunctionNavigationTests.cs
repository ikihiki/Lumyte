using Lumyte.UI.Application.Headless;
using Lumyte.UI.Navigation;
using Xunit;
using static Lumyte.UI.Compose;
using static Lumyte.UI.Navigation.UiNavigationHooks;

namespace Lumyte.UI.Application.Navigation.Tests;

/// <summary>Checks page hosting automatically provides optional navigation to function components.</summary>
public sealed class FunctionNavigationTests
{
    /// <summary>Checks root and nested function components obtain the activation's navigator.</summary>
    /// <param name="nested">Whether another function returns the component.</param>
    /// <returns>The asynchronous verification.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PageFunctionsNavigateWithoutForwardingContextAsync(bool nested)
    {
        IUiNavigator? expected = null;
        IUiNavigator? observed = null;
        Action? click = null;
        bool arrived = false;
        void Created(IUiNavigator navigation, Action callback)
        {
            observed = navigation;
            click = callback;
        }

        UiApplicationBuilder builder = UiApplication.CreateBuilder().UseHeadless().Configure(new() { FrameInterval = TimeSpan.FromMilliseconds(1) });
        builder.MapPage("/", context =>
        {
            expected = context.Activation.Navigation;
            return nested ? NavigationFunctionUi.Parent(Created) : NavigationFunctionUi.Link(Created);
        });
        builder.MapPage("/details", _ =>
        {
            arrived = true;
            return Panel();
        });
        builder.UseNavigation("/");
        await using UiApplication app = builder.Build();
        Task running = app.RunAsync();
        await WaitAsync(app, () => observed is not null && observed.CaptureSnapshot().Phase == UiNavigationPhase.Idle);
        await app.Dispatcher.InvokeAsync(() =>
        {
            Assert.Same(expected, observed);
            Assert.Throws<InvalidOperationException>(() => new UiSurface(NavigationFunctionUi.Link(Created)));
            using var standalone = new UiSurface(ProvideNavigation(expected!, NavigationFunctionUi.Link(Created)));
            Assert.Same(expected, observed);
            click!();
        });
        await WaitAsync(app, () => arrived && expected!.CaptureSnapshot().Phase == UiNavigationPhase.Idle);
        await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await running.WaitAsync(TimeSpan.FromSeconds(10));
    }

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
}
