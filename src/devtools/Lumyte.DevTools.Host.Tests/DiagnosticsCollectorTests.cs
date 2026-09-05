using System.Diagnostics;
using System.Diagnostics.Metrics;
namespace Lumyte.DevTools.Host.Tests;

public sealed class DiagnosticsCollectorTests
{
    [Fact]
    public void CapturesCompletedActivityContract()
    {
        using var collector = new DiagnosticsCollector();
        using var source = new ActivitySource("Lumyte.Test.Activity");
        using (Activity activity = source.StartActivity("Test.Operation", ActivityKind.Internal)!)
        { activity.SetTag("test.value", 42); activity.AddBaggage("tenant", "demo"); activity.AddEvent(new ActivityEvent("checkpoint", tags: new ActivityTagsCollection { { "step", 1 } })); activity.SetStatus(ActivityStatusCode.Error, "broken"); }

        DiagnosticActivity captured = Assert.Single(collector.GetSnapshot().Activities, item => item.Operation == "Test.Operation");
        Assert.NotEmpty(captured.TraceId);
        Assert.NotEmpty(captured.ActivityId);
        Assert.Equal("Error", captured.Status);
        Assert.Equal("broken", captured.StatusDescription);
        Assert.Contains(captured.Tags, tag => tag.Key == "test.value" && tag.Value == "42");
        Assert.Contains(captured.Baggage, tag => tag.Key == "tenant");
        Assert.Equal("checkpoint", Assert.Single(captured.Events).Name);
        Assert.False(captured.IsActive);
    }

    [Fact]
    public void AggregatesCounterUpDownHistogramAndObservableByTags()
    {
        var clock = new TestTimeProvider();
        using var collector = new DiagnosticsCollector(timeProvider: clock);
        using var meter = new Meter("Lumyte.Test.Metrics");
        Counter<long> counter = meter.CreateCounter<long>("requests", "items");
        UpDownCounter<long> active = meter.CreateUpDownCounter<long>("active");
        Histogram<double> latency = meter.CreateHistogram<double>("latency", "ms");
        long gauge = 7;
        _ = meter.CreateObservableGauge("queue", () => gauge);
        counter.Add(2, new KeyValuePair<string, object?>[] { new("route", "a") });
        clock.Advance(TimeSpan.FromSeconds(2));
        counter.Add(4, new KeyValuePair<string, object?>[] { new("route", "a") });
        active.Add(3);
        active.Add(-1);
        latency.Record(10);
        latency.Record(30);
        latency.Record(20);

        DiagnosticsSnapshot snapshot = collector.GetSnapshot();
        MetricSeriesSnapshot requests = Assert.Single(snapshot.Metrics, metric => metric.Instrument == "requests");
        MetricSeriesSnapshot histogram = Assert.Single(snapshot.Metrics, metric => metric.Instrument == "latency");
        Assert.Equal(6, requests.Current);
        Assert.Equal(4, requests.Delta);
        Assert.Equal(2, requests.RatePerSecond);
        Assert.Contains(requests.Tags, tag => tag.Key == "route" && tag.Value == "a");
        Assert.Equal((3L, 60d, 10d, 30d, 20d, 30d), (histogram.Count, histogram.Sum, histogram.Min, histogram.Max, histogram.P50, histogram.P95));
        Assert.Contains(snapshot.Metrics, metric => metric.Instrument == "queue" && metric.Current == 7);
        Assert.Contains(snapshot.Metrics, metric => metric.Instrument == "active" && metric.Current == 2);
    }

    [Fact]
    public void EnforcesCapsAndReportsDrops()
    {
        using var collector = new DiagnosticsCollector(new(ActivityCapacity: 2, SampleCapacity: 3, SeriesCapacity: 2, HistogramWindow: 2));
        using var source = new ActivitySource("Lumyte.Test.Bounds");
        using var meter = new Meter("Lumyte.Test.Bounds");
        Counter<long> counter = meter.CreateCounter<long>("bounded");
        for (int i = 0; i < 4; i++)
        {
            using (source.StartActivity($"operation-{i}"))
            { }
        }

        counter.Add(1, new KeyValuePair<string, object?>[] { new("series", "a") });
        counter.Add(1, new KeyValuePair<string, object?>[] { new("series", "b") });
        counter.Add(1, new KeyValuePair<string, object?>[] { new("series", "c") });
        counter.Add(1, new KeyValuePair<string, object?>[] { new("series", "a") });

        DiagnosticsStatus status = collector.GetStatus();
        Assert.Equal(2, status.ActivityCount);
        Assert.Equal(2, status.DroppedActivities);
        Assert.Equal(2, status.SeriesCount);
        Assert.Equal(1, status.DroppedSeries);
        Assert.True(status.DroppedMetricSamples >= 0);
        Assert.True(status.MetricSampleCount <= 3);
    }

    [Fact]
    public void PauseResumeClearAndConcurrentRecordingAreSafe()
    {
        using var collector = new DiagnosticsCollector();
        using var meter = new Meter("Lumyte.Test.Concurrent");
        Counter<long> counter = meter.CreateCounter<long>("items");
        collector.Pause();
        counter.Add(1);
        Assert.Empty(collector.GetSnapshot().Metrics);
        collector.Resume();
        Parallel.For(0, 100, _ => counter.Add(1));
        MetricSeriesSnapshot series = Assert.Single(
            collector.GetSnapshot().Metrics,
            metric => metric.Meter == "Lumyte.Test.Concurrent" && metric.Instrument == "items");
        Assert.Equal(100, series.Current);
        collector.Clear();
        Assert.Empty(collector.GetSnapshot().Metrics);
        Assert.Equal(0, collector.GetStatus().DroppedSeries);
    }

    private sealed class TestTimeProvider : TimeProvider { private DateTimeOffset now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero); public override DateTimeOffset GetUtcNow() => now; public void Advance(TimeSpan duration) => now += duration; }
}
