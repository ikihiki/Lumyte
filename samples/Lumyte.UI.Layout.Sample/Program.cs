using Lumyte.Graphics.Drawing2D;
using Lumyte.Mathematics.Colors;
using Lumyte.UI;
using Lumyte.UI.Application;
using Lumyte.UI.Application.Headless;
using Lumyte.UI.Automation;
using static Lumyte.UI.Application.Compose;
using static Lumyte.UI.Compose;

int clicks = 0;
UiApplicationWindowContext? context = null;
var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
UiApplicationBuilder builder = UiApplication.CreateBuilder([]).UseHeadless();
builder.UseWindows(_ => [Window(
    "main",
    "One-pass layout",
    root: value =>
{
    context = value;
    Lumyte.UI.Compose.Definitions.Grid grid = Grid(columnSpacing: 10, columns: new UiGridTrack[] { new(80), new(1, true) })[
        GridCell()[Button(testId: "action", automationName: "Action", onClick: () => clicks++, with: [Align.Center()], width: 40, height: 30)[Rectangle(fill: new SRgba<float>(0.2f, 0.6f, 0.9f, 1))]],
        GridCell(column: 1)[Wrap(spacing: 4, runSpacing: 6)[Rectangle(width: 30, height: 20, fill: new SRgba<float>(0.2f, 0.6f, 0.9f, 1)), Rectangle(width: 30, height: 20, fill: new SRgba<float>(0.2f, 0.6f, 0.9f, 1))]]];
    return Flex(orientation: UiOrientation.Horizontal, spacing: 10, with: [Padding.All(8)])[SizedBox(width: 80)[Rectangle(fill: new SRgba<float>(0.2f, 0.6f, 0.9f, 1))], Expanded()[grid]];
},
    onOpened: _ => opened.TrySetResult())]);
await using UiApplication app = builder.Build();
Task run = app.RunAsync();
await opened.Task.WaitAsync(TimeSpan.FromSeconds(10));
var page = new UiAutomationPage(context!.Automation);
await page.GetByTestId("action").ClickAsync();
await app.Dispatcher.InvokeAsync(() =>
{
    UiSurface surface = context.View.CurrentSurface!;
    UiAutomationNode action = surface.CaptureAutomationSnapshot().Nodes.Single(node => node.TestId == "action");
    if (clicks != 1 || action.ContentBounds.Width != 40 || action.ContentBounds.Height != 30
        || action.ContentBounds.X <= 98 || action.ContentBounds.Y < 8)
    {
        throw new InvalidOperationException("Layout, input and automation body geometry diverged.");
    }

    var canvas = new Canvas2D();
    canvas.Begin();
    surface.Record(canvas);
    if (canvas.End().Count == 0)
    {
        throw new InvalidOperationException("The public C4 screen did not record.");
    }
});
await app.StopAsync();
await run;
Console.WriteLine("C4 headless layout passed: Flex, Expanded, Wrap, Grid, GridCell, modifiers, input, automation geometry and recording; no font or theme service.");
return 0;
