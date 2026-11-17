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
[Collection("Application profiling")]
public sealed class ProfilingTests
{
    /// <summary>Checks request/frame joins and repeated processing behind an asynchronous gate.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task CaptureRelationsSurviveHeldGateAsync()
    {
        using var listener = new Capture((EventKeywords)16);
        long request = UiApplicationProfiling.StartCapture();
        var key = new UiApplicationProfiling.Key(Guid.NewGuid(), 42);
        var source = Guid.NewGuid();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        UiApplicationProfiling.Capture(request, 3, key, source, owned: 1, busy: 1);
        Task work = CompleteAsync();
        Assert.False(work.IsCompleted);
        Assert.Contains(listener.Events, item => item.Id == 12 && (int)item.Payload[4]! == 3 && (int)item.Payload[8]! == 1);
        gate.SetResult();
        await work;
        Event[] related = listener.Events.Where(item => item.Id == 12 && (long)item.Payload[0]! == request && (long)item.Payload[2]! == 42).ToArray();
        Assert.Equal(4, related.Length);
        Assert.All(related, item => Assert.Equal(key.Run, item.Payload[1]));
        Assert.Contains(related, item => (int)item.Payload[6]! == 2);
        Assert.DoesNotContain(listener.Events, item => item.Id == 0);

        async Task CompleteAsync()
        {
            await gate.Task;
            UiApplicationProfiling.Capture(request, 4, key, source, processing: 1);
            UiApplicationProfiling.Capture(request, 4, key, source, processing: 2);
            UiApplicationProfiling.Capture(request, 9, key, source, reason: 1);
        }
    }

    /// <summary>Checks opt-in separation from default profiling.</summary>
    [Fact]
    public void DefaultKeywordsDoNotCreateCaptureIdentity()
    {
        using var listener = new Capture((EventKeywords)15);
        Assert.Equal(0, UiApplicationProfiling.StartCapture());
        Assert.DoesNotContain(listener.Events, item => item.Id == 12);
    }

    /// <summary>Checks PNG nesting inside crop and readback.</summary>
    [Fact]
    public void PngScopeIsNestedInsideCrop()
    {
        using var listener = new Capture((EventKeywords)2);
        using Activity activity = new Activity("capture-frame").Start();
        activity.SetCustomProperty("Lumyte.Profiling.RunId", Guid.NewGuid());
        activity.SetCustomProperty("Lumyte.Profiling.FrameId", 64L);
        using (UiApplicationProfiling.Scope? readback = UiApplicationProfiling.Begin(29))
        {
            new UiFramePixels(1, 1, [255, 0, 0, 255]).Crop(new(0, 0, 1, 1));
        }

        Event crop = Assert.Single(listener.Events, item => item.Id == 1 && (int)item.Payload[4]! == 32);
        Event png = Assert.Single(listener.Events, item => item.Id == 1 && (int)item.Payload[4]! == 33);
        Assert.Equal(crop.Payload[2], png.Payload[3]);
        Assert.Single(listener.Events, item => item.Id == 2 && Equals(item.Payload[2], png.Payload[2]));
    }

    /// <summary>Checks default observation buffers metadata without writing normal console output.</summary>
    [Fact]
    public void SampleObserverBuffersSilentlyWithoutTransport()
    {
        TextWriter output = Console.Out;
        TextWriter error = Console.Error;
        using var capturedOutput = new StringWriter();
        using var capturedError = new StringWriter();
        try
        {
            Console.SetOut(capturedOutput);
            Console.SetError(capturedError);
            using var observer = new Lumyte.UI.SampleSupport.UiCaptureDiagnostics();
            long request = UiApplicationProfiling.StartCapture();
            UiApplicationProfiling.Capture(request, 8);
            using var snapshot = System.Text.Json.JsonDocument.Parse(observer.Snapshot());
            Assert.NotEmpty(snapshot.RootElement.GetProperty("events").EnumerateArray());
            Assert.Equal(0, snapshot.RootElement.GetProperty("streamed").GetInt64());
            Assert.Equal(string.Empty, capturedOutput.ToString());
            Assert.Equal(string.Empty, capturedError.ToString());
        }
        finally
        {
            Console.SetOut(output);
            Console.SetError(error);
        }
    }

    /// <summary>Checks the optional transport schema, event budget and reserved terminal delivery.</summary>
    [Fact]
    public void SampleObserverCallbackIsMetadataOnlyAndBounded()
    {
        var records = new List<string>();
        using var observer = new Lumyte.UI.SampleSupport.UiCaptureDiagnostics(records.Add);
        var key = new UiApplicationProfiling.Key(Guid.NewGuid(), 742);
        long request = UiApplicationProfiling.StartCapture();
        UiApplicationProfiling.Capture(request, 3, key);
        for (int i = 0; i < 2050; i++)
        {
            UiApplicationProfiling.Capture(request, 4, key, processing: i);
        }

        Assert.Equal(2048, records.Count);
        UiApplicationProfiling.Capture(request, 10, reason: 1);
        Assert.Equal(2049, records.Count);
        const string Prefix = "UI Capture trace: ";
        foreach (string record in records)
        {
            Assert.StartsWith(Prefix, record);
            using var parsed = System.Text.Json.JsonDocument.Parse(record[Prefix.Length..]);
            System.Text.Json.JsonElement item = parsed.RootElement;
            Assert.Equal(new[] { "eventId", "name", "ticks", "frequency", "data" }, item.EnumerateObject().Select(property => property.Name));
            Assert.Equal(12, item.GetProperty("eventId").GetInt32());
            Assert.NotEmpty(item.GetProperty("name").GetString()!);
            Assert.True(item.GetProperty("frequency").GetInt64() > 0);
            Assert.True(item.GetProperty("ticks").GetInt64() > 0);
            Assert.All(item.GetProperty("data").EnumerateObject(), property => Assert.Contains(property.Value.ValueKind, new[] { System.Text.Json.JsonValueKind.Number, System.Text.Json.JsonValueKind.String, System.Text.Json.JsonValueKind.True, System.Text.Json.JsonValueKind.False, System.Text.Json.JsonValueKind.Null }));
        }

        using var first = System.Text.Json.JsonDocument.Parse(records[1][Prefix.Length..]);
        Assert.Equal(key.Run, first.RootElement.GetProperty("data").GetProperty("profileRunId").GetGuid());
        Assert.Equal(key.Frame, first.RootElement.GetProperty("data").GetProperty("frameId").GetInt64());
        using var last = System.Text.Json.JsonDocument.Parse(records[^1][Prefix.Length..]);
        Assert.Equal(10, last.RootElement.GetProperty("data").GetProperty("phase").GetInt32());
        using var snapshot = System.Text.Json.JsonDocument.Parse(observer.Snapshot());
        Assert.Equal(2049, snapshot.RootElement.GetProperty("streamed").GetInt64());
        Assert.Equal(64, snapshot.RootElement.GetProperty("events").GetArrayLength());
        Assert.True(snapshot.RootElement.GetProperty("dropped").GetInt64() > 0);
    }

    /// <summary>Checks a throwing transport cannot interrupt terminal archiving or later observation.</summary>
    [Fact]
    public void SampleObserverContainsThrowingCallback()
    {
        int calls = 0;
        using var observer = new Lumyte.UI.SampleSupport.UiCaptureDiagnostics(_ =>
        {
            calls++;
            throw new InvalidOperationException("Unavailable diagnostic transport.");
        });
        var key = new UiApplicationProfiling.Key(Guid.NewGuid(), 743);
        using UiApplicationProfiling.Scope? wait = UiApplicationProfiling.Begin(14, key);
        long request = UiApplicationProfiling.StartCapture();
        UiApplicationProfiling.Capture(request, 3, key);
        UiApplicationProfiling.Capture(request, 10, reason: 1);
        UiApplicationProfiling.Capture(request, 6);
        Assert.True(calls >= 4);
        using var snapshot = System.Text.Json.JsonDocument.Parse(observer.Snapshot());
        System.Text.Json.JsonElement terminal = Assert.Single(snapshot.RootElement.GetProperty("requests").EnumerateArray());
        Assert.Equal(10, terminal.GetProperty("data").GetProperty("phase").GetInt32());
        System.Text.Json.JsonElement archive = Assert.Single(snapshot.RootElement.GetProperty("terminalFrames").EnumerateArray());
        Assert.Equal(request, archive.GetProperty("requestId").GetInt64());
        Assert.Contains(archive.GetProperty("openStages").EnumerateArray(), item => item.GetProperty("data").GetProperty("stage").GetInt32() == 14);
        Assert.Empty(snapshot.RootElement.GetProperty("relations").EnumerateArray());
        Assert.Equal(calls, snapshot.RootElement.GetProperty("streamed").GetInt64());
    }

    /// <summary>Checks prior-frame open wait snapshots and reserved terminal recording.</summary>
    [Fact]
    public void SampleObserverIncludesAlreadyOpenWaitAndTerminal()
    {
        using var observer = new Lumyte.UI.SampleSupport.UiCaptureDiagnostics();
        var key = new UiApplicationProfiling.Key(Guid.NewGuid(), 741);
        using UiApplicationProfiling.Scope? wait = UiApplicationProfiling.Begin(14, key);
        long request = UiApplicationProfiling.StartCapture();
        UiApplicationProfiling.Capture(request, 3, key, Guid.NewGuid(), owned: 1, busy: 1);
        using (var snapshot = System.Text.Json.JsonDocument.Parse(observer.Snapshot()))
        {
            Assert.Contains(snapshot.RootElement.GetProperty("events").EnumerateArray(), item => item.GetProperty("eventId").GetInt32() == 1 && item.GetProperty("data").GetProperty("stage").GetInt32() == 14);
        }

        for (int i = 0; i < 2050; i++)
        {
            UiApplicationProfiling.Capture(request, 4, key, processing: i);
        }

        UiApplicationProfiling.Capture(request, 9, key, reason: 2);
        UiApplicationProfiling.Capture(request, 6, key, processing: 2051);
        Assert.True(observer.Snapshot().Length < 65536);
        using var terminal = System.Text.Json.JsonDocument.Parse(observer.Snapshot());
        Assert.True(terminal.RootElement.GetProperty("dropped").GetInt64() > 0);
        Assert.Equal(64, terminal.RootElement.GetProperty("events").GetArrayLength());
        Assert.Contains(terminal.RootElement.GetProperty("requests").EnumerateArray(), item => item.GetProperty("data").GetProperty("phase").GetInt32() == 9);
    }

    /// <summary>Checks zero-key host terminals retain historical joins after live state and ring rollover.</summary>
    /// <param name="phase">The actual host terminal phase.</param>
    [Theory]
    [InlineData(9)]
    [InlineData(10)]
    public void ZeroKeyTerminalRetainsAtTerminalFrameSnapshot(int phase)
    {
        using var observer = new Lumyte.UI.SampleSupport.UiCaptureDiagnostics();
        var key = new UiApplicationProfiling.Key(Guid.NewGuid(), 991);
        UiApplicationProfiling.Scope? wait = UiApplicationProfiling.Begin(14, key);
        long request = UiApplicationProfiling.StartCapture();
        UiApplicationProfiling.Capture(request, 3, key, Guid.NewGuid(), owned: 1, busy: 1);
        UiApplicationProfiling.Capture(request, phase, reason: 1);
        wait?.Dispose();
        for (int i = 0; i < 80; i++)
        {
            using UiApplicationProfiling.Scope? unrelated = UiApplicationProfiling.Begin(15, new(Guid.NewGuid(), 1000 + i));
            UiApplicationProfiling.Capture(request, 6);
        }

        using var snapshot = System.Text.Json.JsonDocument.Parse(observer.Snapshot());
        System.Text.Json.JsonElement archived = Assert.Single(snapshot.RootElement.GetProperty("terminalFrames").EnumerateArray());
        Assert.Equal(request, archived.GetProperty("requestId").GetInt64());
        Assert.Equal(key.Run, archived.GetProperty("profileRunId").GetGuid());
        Assert.Equal(key.Frame, archived.GetProperty("frameId").GetInt64());
        Assert.Equal("atTerminal", archived.GetProperty("observation").GetString());
        Assert.Contains(archived.GetProperty("openStages").EnumerateArray(), item => item.GetProperty("data").GetProperty("stage").GetInt32() == 14);
        System.Text.Json.JsonElement terminal = Assert.Single(snapshot.RootElement.GetProperty("requests").EnumerateArray());
        Assert.Equal(phase, terminal.GetProperty("data").GetProperty("phase").GetInt32());
        Assert.Equal(0, terminal.GetProperty("data").GetProperty("frameId").GetInt64());
        Assert.Empty(snapshot.RootElement.GetProperty("relations").EnumerateArray());
        Assert.DoesNotContain(snapshot.RootElement.GetProperty("openStages").EnumerateArray(), item => item.GetProperty("data").GetProperty("frameId").GetInt64() == key.Frame);
        Assert.True(observer.Snapshot().Length < 65536);

        var nextKey = new UiApplicationProfiling.Key(Guid.NewGuid(), 2000);
        using UiApplicationProfiling.Scope? nextWait = UiApplicationProfiling.Begin(14, nextKey);
        long nextRequest = UiApplicationProfiling.StartCapture();
        UiApplicationProfiling.Capture(nextRequest, 3, nextKey);
        using var next = System.Text.Json.JsonDocument.Parse(observer.Snapshot());
        Assert.Contains(next.RootElement.GetProperty("openStages").EnumerateArray(), item => item.GetProperty("data").GetProperty("frameId").GetInt64() == 2000);
        for (int i = 0; i < 20; i++)
        {
            long newer = UiApplicationProfiling.StartCapture();
            UiApplicationProfiling.Capture(newer, 3, nextKey);
            UiApplicationProfiling.Capture(newer, 8);
        }

        using var evicted = System.Text.Json.JsonDocument.Parse(observer.Snapshot());
        Assert.DoesNotContain(evicted.RootElement.GetProperty("terminalFrames").EnumerateArray(), item => item.GetProperty("requestId").GetInt64() == request);
        Assert.True(observer.Snapshot().Length < 65536);
    }

    /// <summary>Checks the real host preserves supplied cancellation through a held driver request.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task ScreenshotHostReportsCancellationWithoutChangingDriverLifetimeAsync()
    {
        using var listener = new Capture((EventKeywords)16);
        var driver = new CaptureDriver();
        UiApplicationBuilder builder = UiApplication.CreateBuilder();
        builder.UseDriver(() => driver);
        builder.UseWindows(_ => [Lumyte.UI.Application.Compose.Window("main", "Capture", root: _ => Panel(), onOpened: _ => driver.Opened.TrySetResult())]);
        await using UiApplication app = builder.Build();
        Task running = app.RunAsync();
        await driver.Opened.Task;
        using var surface = new UiSurface(Panel());
        var host = (Lumyte.UI.Automation.IUiAutomationScreenshotHost)UiApplicationAutomationHost.Create(app, () => surface);
        using var cancellation = new CancellationTokenSource();
        Task<byte[]> capture = host.CapturePngAsync(cancellation.Token).AsTask();
        await driver.Entered.Task;
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => capture);
        Assert.False(driver.Release.Task.IsCompleted);
        Event terminal = Assert.Single(listener.Events, item => item.Id == 12 && (int)item.Payload[4]! == 9);
        Assert.Equal(1, terminal.Payload[5]);
        Assert.Contains(listener.Events, item => item.Id == 12 && (int)item.Payload[4]! == 2 && Equals(item.Payload[0], terminal.Payload[0]));
        driver.Release.SetResult();
        app.RequestStop();
        await running;
    }

    /// <summary>Checks a disabled stage returns no scope and emits no events.</summary>
    [Fact]
    public void DisabledStagesReturnNoScopeOrEvents()
    {
        using var listener = new Capture((EventKeywords)1);
        Assert.Null(UiApplicationProfiling.Begin(1));
        Assert.Empty(listener.Events);
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
    [Trait("Execution", "Gpu")]
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

    private sealed class CaptureDriver : UiApplicationDriver
    {
        internal TaskCompletionSource Opened { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal override bool SupportsScreenshots => true;

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;

        internal override ValueTask InitializeAsync(UiApplication application, IServiceProvider services, bool textInput, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        internal override ValueTask<UiDriverWindow> CreateWindowAsync(string key, string title, Vector2 size, UiDriverWindow? owner, bool primary, CancellationToken cancellationToken) => ValueTask.FromResult<UiDriverWindow>(new TestWindow(key));

        internal override void Pump()
        {
        }

        internal override Task<bool> DrawAsync(UiDriverWindow window, DrawingSnapshot2D scene) => Task.FromResult(true);

        internal override ValueTask CloseWindowAsync(UiDriverWindow window) => window.DisposeAsync();

        internal override async ValueTask<byte[]> CaptureAsync(UiSurface surface, CancellationToken token)
        {
            Entered.SetResult();
            await Release.Task.WaitAsync(token);
            return [];
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
