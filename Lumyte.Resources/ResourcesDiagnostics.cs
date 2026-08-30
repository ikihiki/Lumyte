using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Lumyte.Resources;

public static class ResourcesDiagnostics
{
    public const string ActivitySourceName = "Lumyte.Resources";

    public const string MeterName = "Lumyte.Resources";

    public static ActivitySource Activities { get; } = new(ActivitySourceName);

    public static Meter Metrics { get; } = new(MeterName);

    internal static Counter<long> LoadRequests { get; } =
        Metrics.CreateCounter<long>("lumyte.resources.load.requests");

    internal static Histogram<double> LoadDuration { get; } =
        Metrics.CreateHistogram<double>(
            "lumyte.resources.load.duration",
            unit: "ms");

    internal static UpDownCounter<long> ActiveLoads { get; } =
        Metrics.CreateUpDownCounter<long>("lumyte.resources.load.active");

    internal static UpDownCounter<long> QueuedLoads { get; } =
        Metrics.CreateUpDownCounter<long>("lumyte.resources.load.queued");

    internal static UpDownCounter<long> ScheduledLoads { get; } =
        Metrics.CreateUpDownCounter<long>("lumyte.resources.load.scheduled");

    internal static Histogram<double> SchedulingWaitDuration { get; } =
        Metrics.CreateHistogram<double>(
            "lumyte.resources.load.scheduling_wait",
            unit: "ms");

    internal static UpDownCounter<long> LoadedResources { get; } =
        Metrics.CreateUpDownCounter<long>("lumyte.resources.loaded");

    internal static Counter<long> ReloadOperations { get; } =
        Metrics.CreateCounter<long>("lumyte.resources.reload.operations");

    internal static Counter<long> ReloadedResources { get; } =
        Metrics.CreateCounter<long>("lumyte.resources.reload.resources");

    internal static Histogram<long> ReloadPropagation { get; } =
        Metrics.CreateHistogram<long>(
            "lumyte.resources.reload.propagated",
            unit: "{resource}");

    internal static Counter<long> CollectionOperations { get; } =
        Metrics.CreateCounter<long>("lumyte.resources.collection.operations");

    internal static Counter<long> UnloadedResources { get; } =
        Metrics.CreateCounter<long>("lumyte.resources.collection.unloaded");

    internal static UpDownCounter<long> MemoryUsage { get; } =
        Metrics.CreateUpDownCounter<long>(
            "lumyte.resources.memory.usage",
            unit: "By");
}
