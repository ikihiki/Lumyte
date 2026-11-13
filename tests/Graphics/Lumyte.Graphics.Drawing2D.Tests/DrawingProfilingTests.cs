using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks the fixed development profiling contract without a native device.</summary>
[Collection("Drawing profiling")]
public sealed class DrawingProfilingTests
{
    /// <summary>Checks direct calls emit unattributed spans and fixed counters without an Activity.</summary>
    /// <returns>The asynchronous test completion.</returns>
    [Fact]
    public async Task DirectWithoutActivityEmitsBalancedSpansAsync()
    {
        using var listener = new Listener((EventKeywords)6);
        using var renderer = new Renderer2D(new DispatchDevice(), new DispatchRenderers().CreateTable());
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        using PreparedDraw2D drawing = await renderer.PrepareAsync(new DrawList2D([]), new() { View = view });
        drawing.Record(new DispatchCommands());
        drawing.Dispose();
        drawing.Dispose();
        Event[] events = listener.Events.ToArray();
        Assert.DoesNotContain(events, item => item.Id == 0);
        Assert.All(events, item =>
        {
            Assert.Equal(Guid.Empty, item.Payload[0]);
            Assert.Equal(0L, item.Payload[1]);
        });
        Assert.Equal(events.Count(item => item.Id == 1), events.Count(item => item.Id == 2));
        Assert.Contains(events, item => item.Id == 3 && Equals(item.Payload[4], 1) && Equals(item.Payload[5], 1) && Equals(item.Payload[6], 0L));
        Assert.Contains(events, item => item.Id == 2 && Equals(item.Payload[4], 11) && Equals(item.Payload[5], 3));
        Event root = Assert.Single(events, item => item.Id == 1 && Equals(item.Payload[4], 1));
        Event child = Assert.Single(events, item => item.Id == 1 && Equals(item.Payload[4], 4));
        Assert.Equal(root.Payload[2], child.Payload[3]);
    }

    /// <summary>Checks nearest typed context survives delayed recording and release.</summary>
    /// <returns>The asynchronous test completion.</returns>
    [Fact]
    public async Task PreparedOwnerRetainsNearestTypedParentAsync()
    {
        using var listener = new Listener((EventKeywords)6);
        var run = Guid.NewGuid();
        using var renderer = new Renderer2D(new DispatchDevice(), new DispatchRenderers().CreateTable());
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        PreparedDraw2D drawing;
        using (Activity parent = new Activity("frame").Start())
        {
            parent.SetCustomProperty("Lumyte.Profiling.RunId", run);
            parent.SetCustomProperty("Lumyte.Profiling.FrameId", 47L);
            using Activity child = new Activity("logical").Start();
            child.SetCustomProperty("Lumyte.Profiling.RunId", "wrong-type");
            drawing = await renderer.PrepareAsync(new DrawList2D([]), new() { View = view });
        }

        drawing.Record(new DispatchCommands());
        drawing.Dispose();
        Event[] events = listener.Events.ToArray();
        Assert.All(events.Where(item => item.Id > 0), item =>
        {
            Assert.Equal(run, item.Payload[0]);
            Assert.Equal(47L, item.Payload[1]);
        });
        Assert.All(events.Where(item => item.Id == 1 && (Equals(item.Payload[4], 10) || Equals(item.Payload[4], 11))), item => Assert.Equal(0L, item.Payload[3]));
    }

    /// <summary>Checks entry validation and cancellation keep their original outcomes.</summary>
    /// <returns>The asynchronous test completion.</returns>
    [Fact]
    public async Task ValidationAndCancellationCloseRootAsync()
    {
        using var listener = new Listener((EventKeywords)2);
        using var renderer = new Renderer2D(new DispatchDevice(), new DispatchRenderers().CreateTable());
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await renderer.PrepareAsync((DrawList2D)null!, new() { View = view }));
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await renderer.PrepareAsync(new DrawList2D([]), new() { View = view }, new CancellationToken(true)));
        Assert.Contains(listener.Events, item => item.Id == 2 && Equals(item.Payload[5], 2));
        Assert.Contains(listener.Events, item => item.Id == 2 && Equals(item.Payload[5], 1));
        Assert.DoesNotContain(listener.Events, item => item.Id == 3);
    }

    /// <summary>Checks counter-only collection and that disabled profiling creates no scope.</summary>
    [Fact]
    public void KeywordFilterAndDisabledScope()
    {
        using (var listener = new Listener((EventKeywords)4))
        {
            using var scope = DrawingProfileScope.Start(1);
            scope?.Counter(1, 9);
            scope?.Success();
            Assert.Single(listener.Events);
            Assert.Equal(3, listener.Events.Single().Id);
        }

        Assert.Null(DrawingProfileScope.Start(1));
    }

    /// <summary>Checks asynchronous spans keep their keys and retained release emits snapshots.</summary>
    /// <returns>The asynchronous test completion.</returns>
    [Fact]
    public async Task AsyncNestingAndRetainedSnapshotsAsync()
    {
        using var listener = new Listener((EventKeywords)6);
        using (var outer = DrawingProfileScope.Start(2))
        {
            await Task.Yield();
            using var inner = DrawingProfileScope.Start(4);
            inner?.Success();
            outer?.Success();
        }

        using var renderer = new Renderer2D(new DispatchDevice(), new DispatchRenderers().CreateTable());
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        using var snapshot = new DrawingSnapshot2D(0, []);
        using PreparedDrawingFrame2D frame = await renderer.PrepareAsync(snapshot, new() { View = view });
        frame.Dispose();
        frame.Dispose();
        Event[] events = listener.Events.ToArray();
        Assert.Contains(events, item => item.Id == 3 && Equals(item.Payload[4], 2) && Equals(item.Payload[5], 11) && Equals(item.Payload[6], 1L));
        Assert.Contains(events, item => item.Id == 3 && Equals(item.Payload[4], 12) && Equals(item.Payload[5], 11) && Equals(item.Payload[6], 0L));
        Assert.Contains(events, item => item.Id == 4 && Equals(item.Payload[3], 3));
        Assert.Contains(events, item => item.Id == 2 && Equals(item.Payload[4], 12) && Equals(item.Payload[5], 3));
        Assert.Equal(events.Count(item => item.Id == 1), events.Count(item => item.Id == 2));
    }

    /// <summary>Checks real rectangle preparation, completion, cache hit and resident recording.</summary>
    /// <returns>The asynchronous test completion.</returns>
    [Fact]
    public async Task ColdRetireCacheHitAndResidentRecordAsync()
    {
        using var listener = new Listener((EventKeywords)6);
        var device = new ProfilingDevice();
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new(1, 2, 3, 4), new SRgba<float>(1, 0, 0, 1));
        using var snapshot = new DrawingSnapshot2D(1, [new(default, canvas.End(), Matrix3x2.Identity, [], 1, 1)]);
        var run = Guid.NewGuid();
        for (int i = 0; i < 2; i++)
        {
            using Activity activity = new Activity("retained-frame").Start();
            activity.SetCustomProperty("Lumyte.Profiling.RunId", run);
            activity.SetCustomProperty("Lumyte.Profiling.FrameId", 81L);
            using PreparedDrawingFrame2D frame = await renderer.PrepareAsync(snapshot, new() { View = view });
            activity.Dispose();
            var commands = new ProfilingCommands();
            frame.Record(commands);
            commands.Submit();
            frame.AttachSubmission(new CompletedSubmission());
            commands.Dispose();
            frame.Retire();
        }

        Event[] events = listener.Events.ToArray();
        Assert.Single(events, item => item.Id == 1 && Equals(item.Payload[4], 3));
        Assert.Single(events, item => item.Id == 1 && Equals(item.Payload[4], 9));
        Assert.Contains(events, item => item.Id == 3 && Equals(item.Payload[4], 2) && Equals(item.Payload[5], 3) && Equals(item.Payload[6], 1L));
        Assert.Contains(events, item => item.Id == 3 && Equals(item.Payload[4], 2) && Equals(item.Payload[5], 4) && Equals(item.Payload[6], 1L));
        Assert.Contains(events, item => item.Id == 3 && Equals(item.Payload[4], 10) && Equals(item.Payload[5], 14) && Equals(item.Payload[6], 1L));
        Assert.Equal(device.CopiedBytes, events.Where(item => item.Id == 3 && Equals(item.Payload[5], 8)).Sum(item => (long)item.Payload[6]!));
        Assert.Equal(1, renderer.RetentionStatistics.ReusedRuns);
        Assert.All(events, item =>
        {
            Assert.Equal(run, item.Payload[0]);
            Assert.Equal(81L, item.Payload[1]);
        });
    }

    /// <summary>Checks the fast run limit and a general rectangle share one outer batch.</summary>
    /// <returns>The asynchronous test completion.</returns>
    [Fact]
    public async Task RectangleFastGeneralRunsAndLimitAsync()
    {
        using var listener = new Listener((EventKeywords)6);
        var device = new ProfilingDevice();
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        var canvas = new Canvas2D();
        canvas.Begin();
        for (int i = 0; i < 1025; i++)
        {
            canvas.FillRect(new(1, 2, 3, 4), new SRgba<float>(1, 0, 0, 1));
        }

        canvas.Scale(Vector2.Zero);
        canvas.FillRect(new(1, 2, 3, 4), new SRgba<float>(1, 0, 0, 1));
        using PreparedDraw2D drawing = await renderer.PrepareAsync(canvas.End(), new() { View = view });
        Event[] events = listener.Events.ToArray();
        Assert.Contains(events, item => item.Id == 3 && Equals(item.Payload[4], 5) && Equals(item.Payload[5], 9) && Equals(item.Payload[6], 3L));
        Assert.Equal(2, events.Count(item => item.Id == 1 && Equals(item.Payload[4], 9)));
        Assert.Contains(events, item => item.Id == 1 && Equals(item.Payload[4], 8));
        Assert.Equal(device.CopiedBytes, events.Where(item => item.Id == 3 && Equals(item.Payload[5], 8)).Sum(item => (long)item.Payload[6]!));
    }

    /// <summary>Checks successful compile precedes fallback and only completed copies are counted on failure.</summary>
    /// <param name="fallback">Whether combined storage is deliberately too small.</param>
    /// <param name="failCopy">The copy to fail, or zero for success.</param>
    /// <returns>The asynchronous test completion.</returns>
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public async Task SharedFallbackAndPartialCopyCountersAsync(bool fallback, int failCopy)
    {
        using var listener = new Listener((EventKeywords)6);
        var device = new ProfilingDevice
        {
            Caps = new() { ShaderTarget = ShaderTarget.SpirV, MaxBufferSize = fallback ? 1UL : 1UL << 30 },
            FailCopy = failCopy,
        };
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillCircle(new(5, 5), 3, new SRgba<float>(1, 0, 0, 1));
        canvas.FillCircle(new(9, 9), 2, new SRgba<float>(0, 1, 0, 1));
        DrawList2D list = canvas.End();
        if (failCopy == 0)
        {
            using PreparedDraw2D drawing = await renderer.PrepareAsync(list, new() { View = view });
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await renderer.PrepareAsync(list, new() { View = view }));
        }

        Event[] events = listener.Events.ToArray();
        Assert.Contains(events, item => item.Id == 2 && Equals(item.Payload[4], 6) && Equals(item.Payload[5], 0));
        Assert.Contains(events, item => item.Id == 1 && Equals(item.Payload[4], fallback ? 8 : 7));
        Assert.DoesNotContain(events, item => item.Id == 1 && Equals(item.Payload[4], fallback ? 7 : 8));
        Assert.Equal(device.CopiedBytes, events.Where(item => item.Id == 3 && Equals(item.Payload[5], 8)).Sum(item => (long)item.Payload[6]!));
        Assert.Equal(events.Count(item => item.Id == 1), events.Count(item => item.Id == 2));
        if (failCopy != 0)
        {
            Assert.True(device.CopiedBytes > 0);
            Assert.Contains(events, item => item.Id == 2 && Equals(item.Payload[4], fallback ? 8 : 7) && Equals(item.Payload[5], 2));
        }
    }

    /// <summary>Checks a custom renderer failure closes nested build and preparation without translation.</summary>
    /// <param name="cancel">Whether the renderer throws cancellation.</param>
    /// <returns>The asynchronous test completion.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RendererFailureClosesNestedStagesAsync(bool cancel)
    {
        using var listener = new Listener((EventKeywords)6);
        Exception failure = cancel ? new OperationCanceledException() : new InvalidOperationException("Injected renderer failure.");
        var strategies = new DispatchRenderers { OnPreparing = _ => throw failure };
        using var renderer = new Renderer2D(new DispatchDevice(), strategies.CreateTable());
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new(1, 2, 3, 4), new SRgba<float>(1, 0, 0, 1));
        Exception? observed = await Record.ExceptionAsync(async () => await renderer.PrepareAsync(canvas.End(), new() { View = view }));
        Assert.Same(failure, observed);
        Event[] events = listener.Events.ToArray();
        Assert.Equal(2, events.Count(item => item.Id == 1));
        Assert.All(events.Where(item => item.Id == 2), item => Assert.Equal(cancel ? 1 : 2, item.Payload[5]));
        Assert.Contains(events, item => item.Id == 3 && Equals(item.Payload[4], 4) && Equals(item.Payload[5], 5) && Equals(item.Payload[6], 0L));
    }

    private sealed class CompletedSubmission : IGraphicsSubmission
    {
        public SubmissionStatus Status => SubmissionStatus.Completed;

        public ValueTask WaitAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed record Event(int Id, object?[] Payload);

    private sealed class Listener : EventListener
    {
        internal Listener(EventKeywords keywords)
        {
            EnableEvents(DrawingProfilingEvents.Log, EventLevel.Informational, keywords);
        }

        internal ConcurrentQueue<Event> Events { get; } = new();

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (eventData.EventSource.Name == "Lumyte-Drawing2D-Profiling")
            {
                Events.Enqueue(new(eventData.EventId, eventData.Payload?.ToArray() ?? []));
            }
        }
    }
}
