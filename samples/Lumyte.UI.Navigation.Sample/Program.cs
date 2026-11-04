using Lumyte.UI.Application;
using Lumyte.UI.Application.Headless;
using Lumyte.UI.Application.Navigation;
using Lumyte.UI.Automation;
using Lumyte.UI.Navigation;
using Lumyte.UI.Navigation.SampleSupport;

UiApplication? application = null;
var pages = new ExamplePages(() => application!.RequestStop());
var content = new UiNavigationApplicationContent(pages, navigator => navigator.RequestNavigate(ExamplePages.Home, default));
UiApplicationBuilder builder = UiApplication.CreateBuilder(args).UseHeadless().UseContent(_ => content);
await using UiApplication app = application = builder.Build();
Task running = app.RunAsync();
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
while (app.State == UiApplicationState.Starting)
{
    await Task.Delay(1, timeout.Token);
}

await WaitAsync(() => content.Navigation.CaptureSnapshot().Phase == UiNavigationPhase.Idle);
UiNavigationWindowSnapshot home = await app.Dispatcher.InvokeAsync(content.WindowContext.View.CaptureNavigationSnapshot);
await new UiAutomationPage(content.WindowContext.Automation).GetByTestId("next").ClickAsync();
await WaitAsync(() => content.Navigation.CaptureSnapshot().Phase == UiNavigationPhase.Idle && content.WindowContext.View.CaptureNavigationSnapshot().Current?.Route == "/details");
UiNavigationWindowSnapshot details = await app.Dispatcher.InvokeAsync(content.WindowContext.View.CaptureNavigationSnapshot);
if (home.Window != details.Window || home.Current!.SurfaceId == details.Current!.SurfaceId)
{
    throw new InvalidOperationException("Navigation did not exchange the surface inside the same window.");
}

await new UiAutomationPage(content.WindowContext.Automation).GetByTestId("quit").ClickAsync();
await running.WaitAsync(timeout.Token);
Console.WriteLine("UI navigation smoke passed: typed destination, fresh surface, stable window and orderly stop.");

async Task WaitAsync(Func<bool> condition)
{
    while (!await app.Dispatcher.InvokeAsync(condition, timeout.Token))
    {
        await Task.Delay(1, timeout.Token);
    }
}
