using Lumyte.UI.Application.Headless;
using Xunit;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Application.Tests;

/// <summary>Checks fixed frame deadlines and wakeups without depending on OS timer accuracy.</summary>
public sealed class FramePacingTests
{
    /// <summary>Small scheduling delays do not accumulate into a lower frame rate.</summary>
    /// <returns>The asynchronous owner-loop checks.</returns>
    [Fact]
    public async Task FrameDeadlinesKeepTheirOriginalPhaseAsync()
    {
        var clock = new Clock();
        UiApplicationBuilder builder = UiApplication.CreateBuilder().Configure(new() { TimeProvider = clock, FrameInterval = TimeSpan.FromSeconds(1d / 120) }).UseHeadless().UseRoot(_ => Panel());
        await using UiApplication app = builder.Build();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Task running = app.RunAsync(timeout.Token);
        while (app.State != UiApplicationState.Running)
        {
            await Task.Delay(1, timeout.Token);
        }

        Task first = app.NextFrameAsync(timeout.Token).AsTask();
        await app.Dispatcher.InvokeAsync(static () => { }, timeout.Token);
        clock.Advance(TimeSpan.FromMilliseconds(8.5));
        await app.Dispatcher.InvokeAsync(static () => { }, timeout.Token);
        await first.WaitAsync(timeout.Token);

        Task second = app.NextFrameAsync(timeout.Token).AsTask();
        await app.Dispatcher.InvokeAsync(static () => { }, timeout.Token);
        clock.Advance(TimeSpan.FromMilliseconds(8.3));
        await app.Dispatcher.InvokeAsync(static () => { }, timeout.Token);
        await second.WaitAsync(timeout.Token);

        // A long pause starts a fresh deadline instead of replaying stale frames.
        Task resumed = app.NextFrameAsync(timeout.Token).AsTask();
        await app.Dispatcher.InvokeAsync(static () => { }, timeout.Token);
        clock.Advance(TimeSpan.FromSeconds(2));
        await app.Dispatcher.InvokeAsync(static () => { }, timeout.Token);
        await resumed.WaitAsync(timeout.Token);
        Task next = app.NextFrameAsync(timeout.Token).AsTask();
        await app.Dispatcher.InvokeAsync(static () => { }, timeout.Token);
        clock.Advance(TimeSpan.FromMilliseconds(4));
        await app.Dispatcher.InvokeAsync(static () => { }, timeout.Token);
        await Task.Delay(20, timeout.Token);
        Assert.False(next.IsCompleted);
        clock.Advance(TimeSpan.FromMilliseconds(5));
        await app.Dispatcher.InvokeAsync(static () => { }, timeout.Token);
        await next.WaitAsync(timeout.Token);
        await app.StopAsync().WaitAsync(timeout.Token);
        await running;
    }

    /// <summary>A queued continuation wakes an owner waiting for its next deadline.</summary>
    /// <returns>The asynchronous wake verification.</returns>
    [Fact]
    public async Task ContinuationsWakeTheOwnerAsync()
    {
        var dispatcher = new UiApplicationDispatcher(new());
        dispatcher.EnableThreadWait();
        using var entered = new ManualResetEventSlim();
        try
        {
            var waiting = Task.Run(() =>
            {
                entered.Set();
                dispatcher.WaitForWork(TimeSpan.FromSeconds(5));
            });
            Assert.True(entered.Wait(TimeSpan.FromSeconds(1)));
            int called = 0;
            dispatcher.Post(_ => called++, null);
            await waiting.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.Equal(0, called);
            dispatcher.Pump();
            Assert.Equal(1, called);
        }
        finally
        {
            dispatcher.ReleaseThreadWait();
        }
    }

    private sealed class Clock : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Interlocked.Read(ref _ticks);

        internal void Advance(TimeSpan time) => Interlocked.Add(ref _ticks, time.Ticks);
    }
}
