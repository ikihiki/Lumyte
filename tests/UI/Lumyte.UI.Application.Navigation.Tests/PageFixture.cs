using Lumyte.UI.Application.Headless;
using Lumyte.UI.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lumyte.UI.Application.Navigation.Tests;

internal sealed class PageFixture : IAsyncDisposable
{
    private readonly Task _running;

    internal PageFixture(Action<UiApplicationBuilder, PageProbe>? configure = null)
    {
        Probe = new();
        UiApplicationBuilder builder = UiApplication.CreateBuilder().UseHeadless().Configure(new() { FrameInterval = TimeSpan.FromMilliseconds(1) });
        builder.Services.AddSingleton(Probe);
        builder.Services.AddScoped<PageService>();
        builder.Services.AddScoped<LifecyclePage>(services =>
        {
            PageService scoped = services.GetRequiredService<PageService>();
            UiPageContext context = services.GetRequiredService<UiPageContext>();
            if (context.Activation.Destination.Route == "/bad" && Probe.FailureStage == "factory")
            {
                throw new ArithmeticException("factory");
            }

            return new(context, Probe, scoped);
        });
        builder.MapPage<LifecyclePage>("/");
        builder.MapPage<LifecyclePage>("/next");
        builder.MapPage<LifecyclePage>("/bad");
        configure?.Invoke(builder, Probe);
        builder.UseNavigation("/");
        App = builder.Build();
        _running = App.RunAsync();
    }

    internal UiApplication App { get; }

    internal PageProbe Probe { get; }

    internal IUiNavigator Navigator => Probe.Visits.Last().Context.Activation.Navigation;

    public async ValueTask DisposeAsync()
    {
        await App.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await _running.WaitAsync(TimeSpan.FromSeconds(10));
        await App.DisposeAsync();
    }

    internal async Task WaitAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (App.State == UiApplicationState.Starting)
        {
            if (App.Completion.IsCompleted)
            {
                await App.Completion;
            }

            await Task.Delay(1, timeout.Token);
        }

        while (!await App.Dispatcher.InvokeAsync(condition, timeout.Token))
        {
            Assert.Null(App.Failure);
            await Task.Delay(1, timeout.Token);
        }
    }

    internal Task ReadyAsync() => WaitAsync(() => Probe.Visits.Count != 0 && Navigator.CaptureSnapshot().Phase == UiNavigationPhase.Idle);

    internal async Task<UiNavigationResult> NavigateAsync(string route)
    {
        UiNavigationRequest request = await App.Dispatcher.InvokeAsync(() => Probe.Visits[0].Context.Activation.Navigation.RequestNavigate(route));
        return await request.Completion.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
