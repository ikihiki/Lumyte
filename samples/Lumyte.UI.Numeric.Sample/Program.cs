using System.Globalization;
using System.Numerics;
using Lumyte.Graphics.Text.Parley;
using Lumyte.Input.Abstractions;
using Lumyte.UI;
using Lumyte.UI.Application;
using Lumyte.UI.Application.Headless;
using Lumyte.UI.Automation;
using Lumyte.UI.Numeric.Sample;
using Lumyte.UI.TextEditing.Parley;
using static Lumyte.UI.Application.Compose;
using static Lumyte.UI.Compose;
using static Lumyte.UI.Controls.Compose;
using static Lumyte.UI.TextEditing.Compose;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: Lumyte.UI.Numeric.Sample <explicit-font.ttf>");
    return 2;
}

using var engine = new ParleyTextEngine2D();
string family;
using (FileStream font = File.OpenRead(args[0]))
{
    family = engine.AddFont(font);
}

var provider = new ParleyUiTextLayoutProvider(engine, new() { FontFamily = family });
using var backend = new NumericTextBackend();
UiApplicationWindowContext? context = null;
var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
int changed = 0;
int committed = 0;
int numericCommits = 0;
UiApplicationBuilder builder = UiApplication.CreateBuilder([]).UseHeadless(new()
{
    ClientSize = new(320, 160),
    TextInputService = backend,
    TextInputTargets = backend,
}).UseTextInput(_ => provider);
builder.UseWindows(_ => [Window(
    "main",
    "Numeric input",
    root: value =>
{
    context = value;
    return Grid(rows: new UiGridTrack[] { new(40), new(40), new(40) })[
        Slider(min: -10, max: 10, step: 1, testId: "scalar", with: [Grid.Cell(), Padding.All(4)], onChanged: _ => changed++, onCommitted: _ => committed++),
        RangeSlider(min: 0, max: 100, step: 5, value: new Lumyte.UI.Controls.SliderRange(20, 80), testId: "range", with: [Grid.Cell(row: 1), Padding.All(4)]),
        NumericBox(value: 12.5, min: -100, max: 100, culture: CultureInfo.GetCultureInfo("fr-FR"), layoutProvider: provider, testId: "editor", onCommitted: _ => numericCommits++, with: [Grid.Cell(row: 2), Padding.All(4)])];
},
    onOpened: _ => opened.TrySetResult())]);
await using UiApplication app = builder.Build();
Task run = app.RunAsync();
await opened.Task.WaitAsync(TimeSpan.FromSeconds(10));
var page = new UiAutomationPage(context!.Automation);
await page.GetByTestId("scalar").SetValueAsync(4);
await page.GetByTestId("range").SetRangeAsync(25, 75);
await page.GetByTestId("editor").FillAsync("-");
await context.Automation.NextFrameAsync();
UiAutomationNode invalid = (await page.SnapshotAsync()).Nodes.Single(node => node.TestId == "editor");
if (invalid.Numeric is not { IsValid: false, Value: 12.5 })
{
    throw new InvalidOperationException("The real NumericBox discarded its incomplete edit.");
}

await page.GetByTestId("editor").FillAsync("23,5");
await context.Automation.NextFrameAsync();
await page.GetByTestId("editor").PressAsync(Key.Enter);
await using (IUiAutomationInputLease lease = await context.Automation.AcquireInputAsync())
{
    await app.Dispatcher.InvokeAsync(() =>
    {
        UiSurface surface = context.View.CurrentSurface!;
        UiAutomationNode scalar = surface.CaptureAutomationSnapshot().Nodes.Single(node => node.TestId == "scalar");
        Vector2 point = new(scalar.ActionableBounds.X + 10, scalar.ActionableBounds.Y + 10);
        lease.Dispatch(surface, new UiPointerInput(lease.PointerId, UiPointerPhase.Down, point));
        lease.Dispatch(surface, new UiPointerInput(lease.PointerId, UiPointerPhase.Move, point + new Vector2(100, 0)));
        lease.Dispatch(surface, new UiPointerInput(lease.PointerId, UiPointerPhase.Up, point + new Vector2(100, 0)));
        lease.Dispatch(surface, new UiKeyInput(Key.End, true));
        lease.Dispatch(surface, new UiKeyInput(Key.End, false));
    });
}

UiAutomationSnapshot snapshot = await page.SnapshotAsync();
if (snapshot.Nodes.Single(node => node.TestId == "scalar").Numeric?.Value != 10
    || snapshot.Nodes.Single(node => node.TestId == "range").Numeric is not { Lower: 25, Upper: 75 }
    || snapshot.Nodes.Single(node => node.TestId == "editor").Numeric is not { Value: 23.5, IsValid: true }
    || changed < 2 || committed < 2 || numericCommits != 1 || backend.Acquisitions < 3 || snapshot.Nodes.Count != 4)
{
    throw new InvalidOperationException("Normal application input did not reach all three real numeric controls.");
}

await app.StopAsync();
await run;
Console.WriteLine("C6 actual Slider/RangeSlider/NumericBox, normal application leases and real Parley editing passed.");
return 0;
