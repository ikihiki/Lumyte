using Lumyte.UI.Application;
using Lumyte.UI.Application.Headless;
using Lumyte.UI.Application.Navigation;
using Lumyte.UI.Automation;
using Lumyte.UI.Navigation;
using Lumyte.UI.Pages.SampleSupport;

UiApplicationBuilder builder = UiApplication.CreateBuilder(args).UseHeadless();
SampleState state = ExamplePages.Configure(builder);
await using UiApplication app = builder.Build();
state.Stop = app.RequestStop;
Task running = app.RunAsync();
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
while (app.State == UiApplicationState.Starting)
{
    if (app.Completion.IsCompleted)
    {
        await app.Completion;
    }

    await Task.Delay(1, timeout.Token);
}

await WaitAsync(() => state.Navigation?.CaptureSnapshot().Phase == UiNavigationPhase.Idle && state.Context!.View.CaptureNavigationSnapshot().Current?.Route == "/");
UiNavigationWindowSnapshot home = await app.Dispatcher.InvokeAsync(state.Context!.View.CaptureNavigationSnapshot);
await new UiAutomationPage(state.Context.Automation).GetByTestId("next").ClickAsync();
await WaitAsync(() => state.Navigation!.CaptureSnapshot().Phase == UiNavigationPhase.Idle && state.Context.View.CaptureNavigationSnapshot().Current?.Route == "/details" && state.Disposed == 1);
UiNavigationWindowSnapshot details = await app.Dispatcher.InvokeAsync(state.Context.View.CaptureNavigationSnapshot);
if (home.Window != details.Window || home.Current!.SurfaceId == details.Current!.SurfaceId || state.Created != 2)
{
    throw new InvalidOperationException("DI pages did not exchange independent scopes and surfaces in the same window.");
}

await new UiAutomationPage(state.Context.Automation).GetByTestId("quit").ClickAsync();
await running.WaitAsync(timeout.Token);
if (state.Disposed != 2)
{
    throw new InvalidOperationException("The final page scope was not disposed exactly once.");
}

Console.WriteLine("UI DI pages smoke passed: inline and class pages, typed context, separate scopes, stable window and orderly stop.");

async Task WaitAsync(Func<bool> condition)
{
    while (!await app.Dispatcher.InvokeAsync(condition, timeout.Token))
    {
        await Task.Delay(1, timeout.Token);
    }
}
