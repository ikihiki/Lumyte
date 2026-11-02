using Lumyte.Animation;
using Lumyte.Core.Time;
using Lumyte.Signals;
using Lumyte.UI;
using Lumyte.UI.Application;
using Lumyte.UI.Application.Headless;
using Lumyte.UI.Automation;
using static Lumyte.UI.Application.Compose;
using static Lumyte.UI.Compose;

int clicks = 0;
int completed = 0;
var clock = new ManualClock();
var target = Signal.Create<float?>(80);
int timelineEvents = 0;
var heightChannel = AnimationChannel<float?>.Create();
var timeline = new AnimationTimelineBuilder();
timeline.Add(Duration.Zero, new Tween<float?>(48, 64, Duration.FromSeconds(1), UiAnimationInterpolators.Nullable(AnimationInterpolators.Float), AnimationEasing.Linear), heightChannel);
timeline.AddEvent(Duration.FromSeconds(0.5), "middle");
UiAnimationClip clip = Animate(timeline.Build(), onEvent: _ => timelineEvents++);
UiApplicationWindowContext? context = null;
var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
UiApplicationBuilder builder = UiApplication.CreateBuilder(args).Configure(new() { AnimationClock = clock }).UseHeadless();
builder.UseWindows(_ => [Window(
    "main",
    "Headless",
    root: value =>
    {
        context = value;
        return Button(width: Animate(target, Duration.FromSeconds(1), UiAnimationInterpolators.Nullable(AnimationInterpolators.Float), AnimationEasing.Linear, () => completed++), height: clip.Value(heightChannel, (float?)48), testId: "save", onClick: () =>
        {
            clicks++;
            target.Value = 160;
        });
    },
    onOpened: _ => opened.TrySetResult())]);
await using UiApplication app = builder.Build();
Task run = app.RunAsync();
await opened.Task.WaitAsync(TimeSpan.FromSeconds(10));
await new UiAutomationPage(context!.Automation).GetByTestId("save").ClickAsync();
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
await WaitAsync(() => context.View.CurrentSurface!.HasRunningAnimations && !context.View.CurrentSurface.HasPendingAnimationUpdates);
await app.Dispatcher.InvokeAsync(() => clock.Advance(Duration.FromSeconds(0.5)));
await WaitAsync(() => context.View.CurrentSurface!.Root.Width == 120 && context.View.CurrentSurface.Root.Height == 56 && timelineEvents == 1);
await app.Dispatcher.InvokeAsync(() => clock.Advance(Duration.FromSeconds(0.5)));
await WaitAsync(() => completed == 1 && context.View.CurrentSurface!.Root.Width == 160);
await app.Dispatcher.InvokeAsync(() =>
{
    if (context.View.CurrentSurface!.Root.Height != 64 || clip.State != AnimationPlaybackState.Completed || timelineEvents != 1)
    {
        throw new InvalidOperationException("Timeline channels or marker delivery did not follow the application clock.");
    }

    if (context.View.CurrentSurface.HasRunningAnimations || context.View.CurrentSurface.HasPendingAnimationUpdates)
    {
        throw new InvalidOperationException("Completed animations retained frame work.");
    }
});
await app.StopAsync();
await run;
if (clicks != 1)
{
    throw new InvalidOperationException("Headless automation did not invoke the callback exactly once.");
}

Console.WriteLine("UI application headless smoke passed: automatic animation frames, manual clock, intermediate/final layout, typed timeline channel, marker, completion and orderly stop.");

async Task WaitAsync(Func<bool> condition)
{
    while (!await app.Dispatcher.InvokeAsync(condition, deadline.Token))
    {
        await Task.Delay(1, deadline.Token);
    }
}
