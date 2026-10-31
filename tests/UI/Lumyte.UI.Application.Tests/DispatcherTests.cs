using Lumyte.UI.Application.Headless;
using Xunit;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Application.Tests;

/// <summary>Checks queue cancellation and capacity at real cross-thread boundaries.</summary>
public sealed class DispatcherTests
{
    /// <summary>Checks cancellation before start skips work while cancellation after start preserves its actual result.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task CancellationOnlySkipsWorkThatHasNotStartedAsync()
    {
        UiApplicationBuilder builder = UiApplication.CreateBuilder().UseHeadless().UseRoot(_ => Panel());
        await using UiApplication app = builder.Build();
        Task run = app.RunAsync();
        await RunningAsync(app);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Task blocker = app.Dispatcher.InvokeAsync(() =>
        {
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
        }).AsTask();
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        using var before = new CancellationTokenSource();
        bool called = false;
        Task queued = app.Dispatcher.InvokeAsync(() => called = true, before.Token).AsTask();
        before.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        release.Set();
        await blocker;
        await app.Dispatcher.InvokeAsync(() => Assert.False(called));
        entered.Reset();
        release.Reset();
        using var after = new CancellationTokenSource();
        Task<int> started = app.Dispatcher.InvokeAsync(
            () =>
        {
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
            return 42;
        },
            after.Token).AsTask();
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        after.Cancel();
        Assert.False(started.IsCompleted);
        release.Set();
        Assert.Equal(42, await started);
        await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await run;
    }

    /// <summary>Checks external queue capacity rejects excess work without running it.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task FullQueueRejectsBeforeMutationAsync()
    {
        UiApplicationBuilder builder = UiApplication.CreateBuilder().Configure(new() { MaximumQueuedUiWork = 1 }).UseHeadless().UseRoot(_ => Panel());
        await using UiApplication app = builder.Build();
        Task run = app.RunAsync();
        await RunningAsync(app);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Task blocking = app.Dispatcher.InvokeAsync(() =>
        {
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
        }).AsTask();
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        int calls = 0;
        Task pending = app.Dispatcher.InvokeAsync(() => calls++).AsTask();
        UiApplicationException error = Assert.Throws<UiApplicationException>(() => app.Dispatcher.InvokeAsync(() => calls++));
        Assert.Equal("ui-queue-full", error.Code);
        release.Set();
        await Task.WhenAll(blocking, pending);
        Assert.Equal(1, calls);
        await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await run;
    }

    private static async Task RunningAsync(UiApplication app)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (app.State != UiApplicationState.Running)
        {
            if (app.Completion.IsCompleted)
            {
                await app.Completion;
            }

            await Task.Delay(1, timeout.Token);
        }
    }
}
