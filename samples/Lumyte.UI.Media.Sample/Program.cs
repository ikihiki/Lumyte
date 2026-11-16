using Lumyte.Core.Time;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Text.Parley;
using Lumyte.Signals;
using Lumyte.UI;
using Lumyte.UI.Application;
using Lumyte.UI.Application.Headless;
using Lumyte.UI.Media;
using Lumyte.UI.Media.Parley;
using static Lumyte.UI.Application.Compose;
using static Lumyte.UI.Compose;
using static Lumyte.UI.Media.Compose;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: Lumyte.UI.Media.Sample <explicit-font.ttf>");
    return 2;
}

using var engine = new ParleyTextEngine2D();
using (FileStream font = File.OpenRead(args[0]))
{
    engine.AddFont(font);
}

var provider = new ParleyTextLayoutProvider(engine);
var clock = new ManualClock();
var progress = Signal.Create(0.25);
var path = new PathBuilder2D();
path.MoveTo(new(0, 0));
path.LineTo(new(12, 0));
path.LineTo(new(6, 12));
path.Close();
var icon = IconSource.Vector(path.Build(), new(0, 0, 12, 12), PaintGraph2D.Solid(new(0.2f, 0.4f, 0.8f, 1)));
UiApplicationWindowContext? context = null;
var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
UiApplicationBuilder builder = UiApplication.CreateBuilder([]).Configure(new() { AnimationClock = clock }).UseHeadless();
builder.UseWindows(_ => [Window(
    "main",
    "Media",
    root: value =>
{
    context = value;
    return Stack(spacing: 4, with: [Padding.All(8)])[
        Text(value: "A real font paragraph wraps once at its final constrained width.", layoutProvider: provider, width: 180, testId: "text"),
        Image(placeholderSize: new System.Numerics.Vector2(24, 12), testId: "image"),
        Icon(source: icon, size: 20, automationName: "Status", testId: "icon"),
        ProgressBar(value: progress, testId: "progress"),
        BusyIndicator(automationName: "Loading", testId: "busy")];
},
    onOpened: _ => opened.TrySetResult())]);
await using UiApplication app = builder.Build();
Task run = app.RunAsync();
await opened.Task.WaitAsync(TimeSpan.FromSeconds(10));

// Opening a window does not guarantee that its first animation phase ran.
// Establish an observed owner-loop baseline before testing a fixed clock delta.
using var animationDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
double baseline = 0;
int baselineFrames = 0;
for (; baselineFrames < 120 && baseline == 0; baselineFrames++)
{
    await app.Dispatcher.InvokeAsync(() => clock.Advance(Duration.FromSeconds(0.125)), animationDeadline.Token);
    await context!.Automation.NextFrameAsync(animationDeadline.Token);
    baseline = await app.Dispatcher.InvokeAsync(
        () => context!.View.CurrentSurface!.RootElement.Children.OfType<BusyIndicatorElement>().Single().Phase,
        animationDeadline.Token);
}

if (baseline == 0)
{
    throw new InvalidOperationException("The application owner loop did not establish a busy animation baseline.");
}

await app.Dispatcher.InvokeAsync(() =>
{
    UiSurface surface = context!.View.CurrentSurface!;
    UiAutomationNode node = surface.CaptureAutomationSnapshot().Nodes.Single(value => value.TestId == "progress");
    if (node.Value != 0.25 || node.Actions != UiAutomationActions.None)
    {
        throw new InvalidOperationException("Read-only progress semantics were not published.");
    }

    progress.Value = 0.75;
    clock.Advance(Duration.FromSeconds(0.25));
});
double observedPhase = baseline;
int progressionFrames = 0;
for (; progressionFrames < 120 && observedPhase == baseline; progressionFrames++)
{
    await context!.Automation.NextFrameAsync(animationDeadline.Token);
    observedPhase = await app.Dispatcher.InvokeAsync(
        () => context!.View.CurrentSurface!.RootElement.Children.OfType<BusyIndicatorElement>().Single().Phase,
        animationDeadline.Token);
}

if (observedPhase == baseline)
{
    throw new InvalidOperationException($"The application owner loop did not advance busy phase: {baseline} -> {observedPhase}.");
}

Console.WriteLine($"Owner-driven busy phase passed: {baseline} -> {observedPhase}; baseline frames={baselineFrames}, progression frames={progressionFrames}.");
await app.Dispatcher.InvokeAsync(() =>
{
    UiSurface surface = context!.View.CurrentSurface!;
    var canvas = new Canvas2D();
    canvas.Begin();
    surface.Record(canvas);
    if (canvas.End().Count == 0 || surface.CaptureAutomationSnapshot().Nodes.Single(value => value.TestId == "progress").Value != 0.75)
    {
        throw new InvalidOperationException("The public media scenario did not record or apply its Signal update.");
    }
});
await app.StopAsync();
await run;
Console.WriteLine("Media headless scenario passed: real Parley layout, explicit image placeholder, vector icon, Signal progress, owner-driven busy clock and orderly cleanup.");
return 0;
