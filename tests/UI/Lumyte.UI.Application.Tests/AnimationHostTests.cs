using Lumyte.Animation;
using Lumyte.Core.Time;
using Lumyte.Signals;
using Lumyte.UI.Application.Headless;
using Xunit;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Application.Tests;

/// <summary>Checks the real standard host drives animations without consumer frame callbacks.</summary>
public sealed class AnimationHostTests
{
    /// <summary>Checks an injected clock drives automatic frames and completion on the application owner.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task StandardHostOwnsTimeAndCompletionDeliveryAsync()
    {
        var clock = new ManualClock();
        var target = Signal.Create<float?>(10);
        UiApplicationWindowContext? context = null;
        int completions = 0;
        UiApplicationBuilder builder = UiApplication.CreateBuilder().Configure(new() { AnimationClock = clock, FrameInterval = TimeSpan.FromMilliseconds(1) }).UseHeadless();
        builder.UseRoot(value =>
        {
            context = value;
            UiAnimated<float?> width = Animate(target, Duration.FromSeconds(1), UiAnimationInterpolators.Nullable(AnimationInterpolators.Float), AnimationEasing.Linear, () =>
            {
                Assert.True(value.Dispatcher.CheckAccess());
                completions++;
            });
            return Rectangle(width: width, height: 20);
        });
        await using UiApplication application = builder.Build();
        Task running = application.RunAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (application.State != UiApplicationState.Running)
        {
            await Task.Delay(1, deadline.Token);
        }

        await application.Dispatcher.InvokeAsync(() => target.Value = 30, deadline.Token);
        await WaitAsync(() => context!.View.CurrentSurface!.HasRunningAnimations);
        await application.Dispatcher.InvokeAsync(() => clock.Advance(Duration.FromSeconds(0.5)), deadline.Token);
        await WaitAsync(() => context!.View.CurrentSurface!.Root.Width == 20);
        await application.Dispatcher.InvokeAsync(() => clock.Advance(Duration.FromSeconds(0.5)), deadline.Token);
        await WaitAsync(() => completions == 1);
        await application.Dispatcher.InvokeAsync(
            () =>
            {
                UiSurface surface = context!.View.CurrentSurface!;
                Assert.Equal(30, surface.Root.Width);
                Assert.False(surface.HasRunningAnimations);
                Assert.False(surface.HasPendingAnimationUpdates);
            },
            deadline.Token);
        await application.StopAsync();
        await running.WaitAsync(deadline.Token);
        Assert.Equal(UiApplicationState.Stopped, application.State);

        async Task WaitAsync(Func<bool> condition)
        {
            while (!await application.Dispatcher.InvokeAsync(condition, deadline.Token))
            {
                await Task.Delay(1, deadline.Token);
            }
        }
    }

    /// <summary>Checks a failed borrowed clock cannot prevent cleanup turns from completing.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task ClockFailureStillDrainsShutdownAsync()
    {
        UiApplicationBuilder builder = UiApplication.CreateBuilder().Configure(new() { AnimationClock = new FailingClock() }).UseHeadless();
        builder.UseRoot(_ => Panel());
        UiApplication application = builder.Build();
        await Assert.ThrowsAsync<ArithmeticException>(() => application.RunAsync().WaitAsync(TimeSpan.FromSeconds(15)));
        Assert.Equal(UiApplicationState.Failed, application.State);
        await Assert.ThrowsAsync<ArithmeticException>(() => application.DisposeAsync().AsTask());
    }

    private sealed class FailingClock : IMonotonicClock
    {
        public TimePoint Now => throw new ArithmeticException("clock-failed");
    }
}
