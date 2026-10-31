using Lumyte.UI.Application;
using Lumyte.UI.Application.Headless;
using Lumyte.UI.Automation;
using static Lumyte.UI.Application.Compose;
using static Lumyte.UI.Compose;

int clicks = 0;
UiApplicationWindowContext? context = null;
var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
UiApplicationBuilder builder = UiApplication.CreateBuilder(args).UseHeadless();
builder.UseWindows(_ => [Window(
    "main",
    "Headless",
    root: value =>
    {
        context = value;
        return Button(width: 160, height: 48, testId: "save", onClick: () => clicks++);
    },
    onOpened: _ => opened.TrySetResult())]);
await using UiApplication app = builder.Build();
Task run = app.RunAsync();
await opened.Task.WaitAsync(TimeSpan.FromSeconds(10));
await new UiAutomationPage(context!.Automation).GetByTestId("save").ClickAsync();
await app.StopAsync();
await run;
if (clicks != 1)
{
    throw new InvalidOperationException("Headless automation did not invoke the callback exactly once.");
}

Console.WriteLine("UI application headless smoke passed.");
