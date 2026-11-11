using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Tests;
using Lumyte.Graphics.Wgpu;
using Lumyte.UI.Application.Headless;
using Lumyte.UI.Windowing;
using Xunit;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Application.Tests;

/// <summary>Checks standard EventSource profiling and retained resource lifecycle events.</summary>
public sealed class ProfilingTests
{
    /// <summary>Checks a disabled provider creates no scopes or per-call allocations.</summary>
    [Fact]
    public void DisabledStagesDoNotAllocate()
    {
        using var listener = new Capture((EventKeywords)1);
        Assert.Null(UiApplicationProfiling.Begin(1));
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 1000; index++)
        {
            UiApplicationProfiling.Begin(1)?.Dispose();
        }

        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }

    /// <summary>Checks typed context lookup, nesting, cancellation and fixed counters.</summary>
    [Fact]
    public void TypedContextAndNestedStagesHaveBalancedPayloads()
    {
        using var listener = new Capture((EventKeywords)6);
        using Activity outer = new Activity("caller").Start();
        var run = Guid.NewGuid();
        outer.SetCustomProperty("Lumyte.Profiling.RunId", run);
        outer.SetCustomProperty("Lumyte.Profiling.FrameId", 73L);
        using Activity child = new Activity("child").Start();
        child.SetCustomProperty("Lumyte.Profiling.RunId", "wrong type");
        using (UiApplicationProfiling.Scope? parent = UiApplicationProfiling.Begin(1))
        {
            using UiApplicationProfiling.Scope? nested = UiApplicationProfiling.Begin(2);
            Assert.NotNull(parent);
            Assert.NotNull(nested);
            Assert.Equal(parent.Span, nested.Parent);
            Assert.Equal(new(run, 73), nested.Key);
            nested.Counter(18, 42);
            nested.Failed(new OperationCanceledException());
        }

        Assert.DoesNotContain(listener.Events, item => item.Id == 0);
        Assert.Contains(listener.Events, item => item.Id == 3 && (int)item.Payload[5]! == 18 && (long)item.Payload[6]! == 42);
        Assert.Contains(listener.Events, item => item.Id == 2 && (int)item.Payload[4]! == 2 && (int)item.Payload[5]! == 1);
    }

    /// <summary>Checks counter-only filtering and uncorrelated values.</summary>
    [Fact]
    public void CounterKeywordDoesNotEmitStages()
    {
        using var listener = new Capture((EventKeywords)4);
        using (UiApplicationProfiling.Scope? scope = UiApplicationProfiling.Begin(17, default(UiApplicationProfiling.Key)))
        {
            scope?.Counter(16, 1);
        }

        Assert.DoesNotContain(listener.Events, item => item.Id is 1 or 2 or 0);
        Assert.Contains(listener.Events, item => item.Id == 3 && (Guid)item.Payload[0]! == Guid.Empty && (long)item.Payload[1]! == 0);
    }

    /// <summary>Checks confirmed retirement and successful release are emitted once from saved frame keys.</summary>
    [Fact]
    public void FrameReleaseAndRetirementAreNotDuplicated()
    {
        using var listener = new Capture((EventKeywords)7);
        using Activity activity = new Activity("frame").Start();
        var run = Guid.NewGuid();
        activity.SetCustomProperty("Lumyte.Profiling.RunId", run);
        activity.SetCustomProperty("Lumyte.Profiling.FrameId", 91L);
        var frame = new UiHostPresenter.Frame(Guid.NewGuid());
        activity.Stop();
        frame.ConfirmDeviceRetirement();
        frame.ConfirmDeviceRetirement();
        frame.Dispose();
        frame.Dispose();
        Assert.Single(listener.Events, item => item.Id == 8 && (Guid)item.Payload[0]! == run && (long)item.Payload[1]! == 91);
        Assert.Single(listener.Events, item => item.Id == 9 && (Guid)item.Payload[0]! == run && (long)item.Payload[1]! == 91);
    }

    /// <summary>Checks Submitted does not emit a terminal flight counter and release reports one snapshot.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task QueueFlightReportsOneTerminalSnapshotAsync()
    {
        using var listener = new Capture((EventKeywords)7);
        using var gate = new SemaphoreSlim(1, 1);
        var queue = new UiFrameSubmissionQueue(gate, 2);
        UiFrameSubmissionQueue.Flight first = await queue.EnterAsync();
        first.Submitted();
        UiFrameSubmissionQueue.Flight second = await queue.EnterAsync();
        second.Submitted();
        first.Dispose();
        first.Dispose();
        second.Dispose();
        Event[] starts = listener.Events.Where(item => item.Id == 1 && (int)item.Payload[4]! == 20).ToArray();
        Assert.Equal(2, starts.Length);
        foreach (Event start in starts)
        {
            Event counter = Assert.Single(listener.Events, item => item.Id == 3 && (int)item.Payload[4]! == 20 && (int)item.Payload[5]! == 17 && Equals(item.Payload[2], start.Payload[2]));
            Assert.Equal(0L, counter.Payload[6]);
            Assert.Single(listener.Events, item => item.Id == 2 && Equals(item.Payload[2], start.Payload[2]));
        }

        Assert.Contains(listener.Events, item => item.Id == 3 && (int)item.Payload[5]! == 16 && (long)item.Payload[6]! == 0);
    }

    /// <summary>Checks headless owner turns issue and finish real correlated frames.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task HeadlessFramesHaveMatchingControlEventsAsync()
    {
        using var listener = new Capture((EventKeywords)7);
        UiApplicationWindowContext? context = null;
        UiApplicationBuilder builder = UiApplication.CreateBuilder().Configure(new() { FrameInterval = TimeSpan.FromMilliseconds(1) });
        builder.UseHeadless().UseRoot(value =>
        {
            context = value;
            return Panel();
        });
        await using UiApplication app = builder.Build();
        Task running = app.RunAsync();
        try
        {
            for (int attempt = 0; app.State != UiApplicationState.Running && attempt < 500; attempt++)
            {
                await Task.Delay(10);
            }

            Assert.NotNull(context);
            await context.Automation.NextFrameAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await running.WaitAsync(TimeSpan.FromSeconds(10));
        }

        Event[] accepted = listener.Events.Where(item => item.Id == 4).ToArray();
        Assert.NotEmpty(accepted);
        foreach (Event item in accepted)
        {
            Assert.NotEqual(Guid.Empty, (Guid)item.Payload[0]!);
            Assert.True((long)item.Payload[1]! > 0);
            Assert.Single(listener.Events, end => end.Id == 5 && Equals(end.Payload[0], item.Payload[0]) && Equals(end.Payload[1], item.Payload[1]));
        }

        Assert.DoesNotContain(listener.Events, item => item.Id == 0);
    }

    /// <summary>Checks observed work counters survive a later driver exception.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task DriverFailurePreservesPartialCountersAsync()
    {
        using var listener = new Capture((EventKeywords)7);
        var driver = new FailingDriver();
        UiApplicationBuilder builder = UiApplication.CreateBuilder().Configure(new() { FrameInterval = TimeSpan.FromMilliseconds(1) });
        builder.UseDriver(() => driver);
        builder.UseRoot(_ => Panel());
        UiApplication app = builder.Build();
        Task running = app.RunAsync();
        await Assert.ThrowsAnyAsync<Exception>(() => running.WaitAsync(TimeSpan.FromSeconds(10)));
        await Assert.ThrowsAnyAsync<Exception>(() => app.DisposeAsync().AsTask());
        Event failed = Assert.Single(listener.Events, item => item.Id == 2 && (int)item.Payload[4]! == 7 && (int)item.Payload[5]! == 2);
        Event completed = Assert.Single(listener.Events, item => item.Id == 3 && Equals(item.Payload[2], failed.Payload[2]) && (int)item.Payload[5]! == 6);
        Assert.Equal(1L, completed.Payload[6]);
        Assert.Single(listener.Events, item => item.Id == 3 && Equals(item.Payload[2], failed.Payload[2]) && (int)item.Payload[5]! == 7);
        Event advance = Assert.Single(listener.Events, item => item.Id == 2 && (int)item.Payload[4]! == 4 && (int)item.Payload[5]! == 2);
        Assert.Single(listener.Events, item => item.Id == 3 && Equals(item.Payload[2], advance.Payload[2]) && (int)item.Payload[5]! == 1 && (long)item.Payload[6]! == 1);
    }

    /// <summary>Checks real presenter terminal counters, owner relation and confirmed retirement.</summary>
    /// <returns>The verification.</returns>
    [GpuFact]
    public async Task PresenterTerminalCountersAndOwnerRelationAreUniqueAsync()
    {
        using var listener = new Capture((EventKeywords)7);
        using var device = WgpuDevice.Create();
        using var presenter = new UiHostPresenter(device, default);
        using var surface = new UiSurface(Panel());
        surface.SetViewport(new(16, 16));
        using UiDrawingScene2D retained = surface.CreateDrawingScene();
        retained.Update();
        using DrawingSnapshot2D scene = retained.Scene.CaptureSnapshot();
        var owner = Guid.NewGuid();
        using var swapchain = new TimeoutSwapchain();
        Assert.Equal(SurfaceStatus.Timeout, await presenter.DrawAsync(swapchain, scene, owner));
        await presenter.DrainAsync(owner);
        await presenter.ReleaseAfterDeviceRetirementAsync();
        foreach (int stage in new[] { 8, 21, 23 })
        {
            Event end = Assert.Single(listener.Events, item => item.Id == 2 && (int)item.Payload[4]! == stage);
            Event count = Assert.Single(listener.Events, item => item.Id == 3 && Equals(item.Payload[2], end.Payload[2]) && (int)item.Payload[5]! == 15);
            Assert.Equal(0L, count.Payload[6]);
        }

        Event drain = Assert.Single(listener.Events, item => item.Id == 1 && (int)item.Payload[4]! == 21);
        Assert.Single(listener.Events, item => item.Id == 10 && Equals(item.Payload[0], drain.Payload[2]) && Equals(item.Payload[1], owner));
    }

    /// <summary>Checks stages alone stay uncorrelated and reconnect reuses a host run without reusing frame IDs.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task HostReconnectPreservesRunAndRestoresCallerActivityAsync()
    {
        using var listener = new Capture((EventKeywords)2);
        using Activity caller = new Activity("caller").Start();
        var driver = new ObservingDriver();
        UiApplicationBuilder builder = UiApplication.CreateBuilder().Configure(new() { FrameInterval = TimeSpan.FromMilliseconds(1) });
        builder.UseDriver(() => driver).UseRoot(_ => Panel());
        await using UiApplication app = builder.Build();
        Task running = app.RunAsync();
        Assert.Same(caller, Activity.Current);
        try
        {
            await WaitForAsync(() => !driver.Keys.IsEmpty);
            Assert.All(driver.Keys, key => Assert.Equal(default, key));
            listener.SetKeywords((EventKeywords)7);
            await WaitForAsync(() => driver.Keys.Any(key => key.Frame > 0));
            UiApplicationProfiling.Key first = driver.Keys.First(key => key.Frame > 0);
            listener.SetKeywords((EventKeywords)2);
            int before = driver.Keys.Count;
            await WaitForAsync(() => driver.Keys.Skip(before).Any(key => key.Frame == 0));
            long previous = driver.Keys.Max(key => key.Frame);
            listener.SetKeywords((EventKeywords)7);
            await WaitForAsync(() => driver.Keys.Any(key => key.Frame > previous));
            UiApplicationProfiling.Key next = driver.Keys.First(key => key.Frame > previous);
            Assert.Equal(first.Run, next.Run);
            Assert.NotEqual(Guid.Empty, next.Run);
            Assert.True(next.Frame > first.Frame);
            Event metadata = Assert.Single(listener.Events, item => item.Id == 11);
            Assert.Equal(first.Run, metadata.Payload[0]);
            Assert.All(metadata.Payload.Skip(1).Take(5), value => Assert.IsType<string>(value));
            Assert.All(metadata.Payload.Skip(6), value => Assert.Equal("unknown", value));
            await WaitForAsync(() => listener.Events.Count(item => item.Id == 11) >= 2);
            Assert.All(listener.Events.Where(item => item.Id == 11), item => Assert.Equal(first.Run, item.Payload[0]));
        }
        finally
        {
            await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await running.WaitAsync(TimeSpan.FromSeconds(10));
        }

        Assert.Same(caller, Activity.Current);
        Assert.True(driver.ContinuationsMatched);
    }

    /// <summary>Checks actual failed waits and disposal retries retain failure outcomes and release only once.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task FrameRetriesKeepFailureOutcomesAndOneReleaseAsync()
    {
        using var listener = new Capture((EventKeywords)7);
        var submission = new ProbeSubmission();
        var frame = new UiHostPresenter.Frame(Guid.NewGuid()) { Submission = submission };
        frame.Own(new RetryResource());
        await Assert.ThrowsAsync<ArithmeticException>(() => frame.DrainAsync().AsTask());
        await frame.DrainAsync();
        await frame.DrainAsync();
        Assert.Throws<ArithmeticException>(frame.Dispose);
        Assert.DoesNotContain(listener.Events, item => item.Id == 9);
        frame.Dispose();
        frame.Dispose();
        Assert.Equal(2, submission.Waits);
        Event[] waits = listener.Events.Where(item => item.Id == 2 && (int)item.Payload[4]! == 14).ToArray();
        Assert.Equal(2, waits.Length);
        Assert.Equal(2, waits[0].Payload[5]);
        Assert.Equal(0, waits[1].Payload[5]);
        Assert.NotEqual(waits[0].Payload[2], waits[1].Payload[2]);
        Assert.Contains(listener.Events, item => item.Id == 2 && (int)item.Payload[4]! == 16 && (int)item.Payload[5]! == 2);
        Assert.Single(listener.Events, item => item.Id == 9);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(1, timeout.Token);
        }
    }

    private sealed class ObservingDriver : UiApplicationDriver
    {
        internal ConcurrentQueue<UiApplicationProfiling.Key> Keys { get; } = new();

        internal bool ContinuationsMatched { get; private set; } = true;

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;

        internal override ValueTask InitializeAsync(UiApplication application, IServiceProvider services, bool textInput, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        internal override ValueTask<UiDriverWindow> CreateWindowAsync(string key, string title, Vector2 size, UiDriverWindow? owner, bool primary, CancellationToken cancellationToken) => ValueTask.FromResult<UiDriverWindow>(new TestWindow(key));

        internal override void Pump()
        {
        }

        internal override async Task<bool> DrawAsync(UiDriverWindow window, DrawingSnapshot2D scene)
        {
            UiApplicationProfiling.Key key = UiApplicationProfiling.ReadKey();
            Keys.Enqueue(key);
            await Task.Delay(1);
            ContinuationsMatched &= key == UiApplicationProfiling.ReadKey();
            return true;
        }

        internal override ValueTask CloseWindowAsync(UiDriverWindow window) => window.DisposeAsync();
    }

    private sealed class ProbeSubmission : IGraphicsSubmission
    {
        public SubmissionStatus Status => SubmissionStatus.Completed;

        internal int Waits { get; private set; }

        public void Dispose()
        {
        }

        public ValueTask WaitAsync(CancellationToken cancellationToken = default)
        {
            Waits++;
            return Waits == 1 ? ValueTask.FromException(new ArithmeticException()) : ValueTask.CompletedTask;
        }
    }

    private sealed class RetryResource : IDisposable
    {
        private bool _failed;

        public void Dispose()
        {
            if (!_failed)
            {
                _failed = true;
                throw new ArithmeticException();
            }
        }
    }

    private sealed class TimeoutSwapchain : IGraphicsSwapchain
    {
        public SwapchainDesc Configuration { get; } = new() { Width = 16, Height = 16, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment };

        public void Dispose()
        {
        }

        public void Reconfigure(SwapchainDesc desc) => throw new NotSupportedException();

        public ValueTask<SurfaceAcquireResult> AcquireNextFrameAsync(IGraphicsSemaphore? signalSemaphore = null, CancellationToken cancellationToken = default) => ValueTask.FromResult(new SurfaceAcquireResult(SurfaceStatus.Timeout, null));
    }

    private sealed class FailingDriver : UiApplicationDriver
    {
        private bool _drawn;
        private bool _failedAdvance;
        private bool _failedComplete;

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;

        internal override ValueTask InitializeAsync(UiApplication application, IServiceProvider services, bool textInput, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        internal override ValueTask<UiDriverWindow> CreateWindowAsync(string key, string title, Vector2 size, UiDriverWindow? owner, bool primary, CancellationToken cancellationToken) => ValueTask.FromResult<UiDriverWindow>(new TestWindow(key));

        internal override void Pump()
        {
        }

        internal override Task<bool> DrawAsync(UiDriverWindow window, DrawingSnapshot2D scene)
        {
            _drawn = true;
            return Task.FromResult(true);
        }

        internal override bool AdvanceFrames()
        {
            if (_drawn && !_failedAdvance)
            {
                _failedAdvance = true;
                throw new ArithmeticException();
            }

            return false;
        }

        internal override void CompleteFrames()
        {
            if (_drawn && !_failedComplete)
            {
                _failedComplete = true;
                throw new ArithmeticException();
            }
        }

        internal override ValueTask CloseWindowAsync(UiDriverWindow window) => window.DisposeAsync();
    }

    private sealed class TestWindow(string key) : UiDriverWindow(key)
    {
        private readonly Guid _source = Guid.NewGuid();
        private bool _visible;
        private bool _closed;

        public override ValueTask DisposeAsync()
        {
            _closed = true;
            return ValueTask.CompletedTask;
        }

        internal override UiWindowSourceSnapshot CaptureState() => new(1, null, new(20, 10), Vector2.One, _visible, _visible, _closed, _source);

        internal override void Show() => _visible = true;

        internal override void SetTitle(string title)
        {
        }
    }

    private sealed record Event(int Id, object?[] Payload);

    private sealed class Capture : EventListener
    {
        internal Capture(EventKeywords keywords)
        {
            EnableEvents(UiApplicationProfiling.Log, EventLevel.Informational, keywords);
        }

        internal ConcurrentQueue<Event> Events { get; } = new();

        internal void SetKeywords(EventKeywords keywords)
        {
            DisableEvents(UiApplicationProfiling.Log);
            EnableEvents(UiApplicationProfiling.Log, EventLevel.Informational, keywords);
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (eventData.EventSource.Name == "Lumyte-UI-Application-Profiling")
            {
                Events.Enqueue(new(eventData.EventId, eventData.Payload?.ToArray() ?? []));
            }
        }
    }
}
