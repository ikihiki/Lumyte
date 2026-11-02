using Lumyte.UI.Application;
using Lumyte.UI.Application.Headless;
using Lumyte.UI.Automation;
using Lumyte.UI.Navigation;
using Lumyte.UI.Transitions.SampleSupport;

var scene = new TransitionScene();
UiApplicationBuilder builder = UiApplication.CreateBuilder(args).UseHeadless();
scene.Configure(builder);
await using UiApplication app = builder.Build();
scene.Application = app;
Task running = app.RunAsync();
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
while (app.State == UiApplicationState.Starting)
{
    if (app.Completion.IsCompleted)
    {
        await app.Completion;
    }

    await Task.Delay(1, deadline.Token);
}

await WaitAsync(() => scene.Navigation?.CaptureSnapshot().Phase == UiNavigationPhase.Idle && scene.Entered == 1);
await app.Dispatcher.InvokeAsync(() => scene.PopupOpen.Value = true);
await WaitAsync(() => scene.Context!.Popups.CapturePopups().Any(popup => popup.State == Lumyte.UI.Windowing.UiPopupState.Open) || scene.PopupClosed != 0);
if (scene.PopupClosed != 0)
{
    throw new InvalidOperationException($"Popup did not open: {scene.PopupCloseCode}");
}

await app.Dispatcher.InvokeAsync(() => scene.PopupOpen.Value = false);
await WaitAsync(() => scene.PopupClosed == 1);
await new UiAutomationPage(scene.Context!.Automation).GetByTestId("next").ClickAsync();
UiVisualTransitionResult visual = await scene.LastRequest!.VisualCompletion.WaitAsync(deadline.Token);
if (visual.Status != UiVisualTransitionStatus.Skipped || visual.Code != "capture-unavailable")
{
    throw new InvalidOperationException("Headless visual fallback did not preserve the navigation outcome.");
}

await WaitAsync(() => scene.Navigation!.CaptureSnapshot().Phase == UiNavigationPhase.Idle && scene.Disposed == 1);
await new UiAutomationPage(scene.Context.Automation).GetByTestId("quit").ClickAsync();
await running.WaitAsync(deadline.Token);
if (scene.Disposed != 2)
{
    throw new InvalidOperationException("Page scopes did not retire exactly once.");
}

Console.WriteLine("UI transitions smoke passed: static Compose, presence fallback, visual completion, DI scope retirement and ordinary automation.");

async Task WaitAsync(Func<bool> condition)
{
    while (!await app.Dispatcher.InvokeAsync(condition, deadline.Token))
    {
        await Task.Delay(1, deadline.Token);
    }
}
