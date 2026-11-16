using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Text.Parley;
using Lumyte.Signals;
using Lumyte.UI;
using Lumyte.UI.Application;
using Lumyte.UI.Application.Headless;
using Lumyte.UI.Automation;
using Lumyte.UI.Media.Parley;
using static Lumyte.UI.Application.Compose;
using static Lumyte.UI.Compose;
using static Lumyte.UI.Controls.Compose;
using static Lumyte.UI.Media.Compose;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: Lumyte.UI.Controls.Sample <explicit-font.ttf>");
    return 2;
}

using var engine = new ParleyTextEngine2D();
using (FileStream font = File.OpenRead(args[0]))
{
    engine.AddFont(font);
}

var provider = new ParleyTextLayoutProvider(engine);
var sound = Signal.Create(false);
var quality = Signal.Create<string?>("balanced");
int changes = 0;
UiApplicationWindowContext? context = null;
var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
UiApplicationBuilder builder = UiApplication.CreateBuilder([]).UseHeadless();
builder.UseWindows(_ => [Window(
    "main",
    "Settings",
    root: value =>
{
    context = value;
    Lumyte.UI.Controls.Compose.Definitions.Switch soundControl = Switch(
        testId: "sound",
        automationName: "Sound",
        isOn: sound,
        onChanged: enabled =>
        {
            changes++;
            sound.Value = enabled;
        })[Text(value: "Sound", layoutProvider: provider)];
    Lumyte.UI.Controls.Compose.Definitions.RadioGroup qualityControl = RadioGroup(testId: "quality", automationName: "Quality", selectedKey: quality, onChanged: key => quality.Value = key)[
        RadioButton(key: "balanced", automationName: "Balanced")[Text(value: "Balanced", layoutProvider: provider)],
        RadioButton(key: "high", automationName: "High")[Text(value: "High", layoutProvider: provider)]];
    return Stack(spacing: 6, with: [Padding.All(8)])[
        Text(value: "Settings", layoutProvider: provider),
        ToggleButton(testId: "toggle", automationName: "Preview")[Text(value: "Preview", layoutProvider: provider)],
        CheckBox(testId: "check", automationName: "Notifications", threeState: true)[Text(value: "Notifications", layoutProvider: provider)],
        soundControl,
        qualityControl];
},
    onOpened: _ => opened.TrySetResult())]);
await using UiApplication app = builder.Build();
Task run = app.RunAsync();
await opened.Task.WaitAsync(TimeSpan.FromSeconds(10));
var page = new UiAutomationPage(context!.Automation);
await page.GetByTestId("sound").CheckAsync();
await page.GetByTestId("sound").CheckAsync();
await page.GetByTestId("check").CheckAsync();
await page.GetByTestId("check").UncheckAsync();
await page.GetByTestId("toggle").ClickAsync();
await page.GetByTestId("quality").SelectOptionAsync("high");
await app.Dispatcher.InvokeAsync(() =>
{
    UiSurface surface = context.View.CurrentSurface!;
    UiAutomationSnapshot snapshot = surface.CaptureAutomationSnapshot();
    if (!sound.Value || changes != 1 || quality.Value != "high"
        || snapshot.Nodes.Single(node => node.TestId == "check").IsChecked != false)
    {
        throw new InvalidOperationException("Settings input, model and semantic state diverged.");
    }

    var canvas = new Canvas2D();
    canvas.Begin();
    surface.Record(canvas);
    if (canvas.End().Count == 0)
    {
        throw new InvalidOperationException("The public settings screen did not record.");
    }
});
await app.StopAsync();
await run;
Console.WriteLine("Controls headless settings passed: five typed widgets, explicit font labels, Signal writeback, desired-state idempotence and orderly shutdown.");
return 0;
