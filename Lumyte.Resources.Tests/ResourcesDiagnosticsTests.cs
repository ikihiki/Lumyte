using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;

namespace Lumyte.Resources.Tests;

public sealed class ResourcesDiagnosticsTests
{
    [Fact]
    public async Task LoadReportsCacheOutcomeAndActivityTags()
    {
        var activities = new ConcurrentQueue<Activity>();
        using ActivityListener activityListener = CreateActivityListener(activities);
        var measurements = new ConcurrentQueue<Measurement>();
        using MeterListener meterListener = CreateMeterListener(measurements);
        ResourceStore store = CreateStore();
        AssetKey<TestResource> key = Asset.From<TestResource>("memory:item");

        await store.LoadAsync(key);
        await store.LoadAsync(key);

        Assert.Contains(
            activities,
            activity =>
                activity.OperationName == "ResourceStore.Load"
                && Equals(activity.GetTagItem("asset.scheme"), "memory")
                && Equals(activity.GetTagItem("resource.cache.hit"), false));
        Assert.Contains(
            measurements,
            measurement =>
                measurement.Name == "lumyte.resources.load.requests"
                && measurement.HasTag("outcome", "succeeded")
                && measurement.HasTag("cache.hit", true));
    }

    [Fact]
    public async Task ReloadReportsGenerationAndPropagation()
    {
        var activities = new ConcurrentQueue<Activity>();
        using ActivityListener activityListener = CreateActivityListener(activities);
        var measurements = new ConcurrentQueue<Measurement>();
        using MeterListener meterListener = CreateMeterListener(measurements);
        ResourceStore store = CreateStore();
        AssetKey<TestResource> key = Asset.From<TestResource>("memory:item");
        await store.LoadAsync(key);

        await store.ReloadAsync(key);

        Assert.Contains(
            activities,
            activity =>
                activity.OperationName == "ResourceStore.Reload"
                && Equals(activity.GetTagItem("resource.generation"), 1u)
                && Equals(activity.GetTagItem("resource.reload.propagated"), 0));
        Assert.Contains(
            measurements,
            measurement =>
                measurement.Name == "lumyte.resources.reload.operations"
                && measurement.HasTag("outcome", "succeeded"));
    }

    private static ResourceStore CreateStore() =>
        new([new TestResolver()], [new TestLoader()]);

    private static ActivityListener CreateActivityListener(
        ConcurrentQueue<Activity> activities)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ResourcesDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllData,
            ActivityStopped = activities.Enqueue,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static MeterListener CreateMeterListener(
        ConcurrentQueue<Measurement> measurements)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == ResourcesDiagnostics.MeterName)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>(
            (instrument, value, tags, _) =>
                measurements.Enqueue(new(instrument.Name, value, tags.ToArray())));
        listener.SetMeasurementEventCallback<double>(
            (instrument, value, tags, _) =>
                measurements.Enqueue(new(instrument.Name, value, tags.ToArray())));
        listener.Start();
        return listener;
    }

    private sealed record Measurement(
        string Name,
        double Value,
        IReadOnlyList<KeyValuePair<string, object?>> Tags)
    {
        public bool HasTag(string name, object value) =>
            Tags.Any(tag => tag.Key == name && Equals(tag.Value, value));
    }

    private sealed record TestResource(string Text);

    private sealed class TestResolver : IAssetResolver
    {
        public string Scheme => "memory";

        public ValueTask<AssetData> OpenAsync(
            AssetAddress address,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(
                new AssetData(
                    new MemoryStream(Encoding.UTF8.GetBytes("value")),
                    new AssetLocation("memory", address.ToString())));
    }

    private sealed class TestLoader : IResourceLoader<TestResource>
    {
        public async ValueTask<TestResource> LoadAsync(
            ResourceLoadContext context,
            CancellationToken cancellationToken = default)
        {
            using StreamReader reader = new(
                context.Content,
                Encoding.UTF8,
                leaveOpen: true);
            return new TestResource(await reader.ReadToEndAsync(cancellationToken));
        }
    }
}
