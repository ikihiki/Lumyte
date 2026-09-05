namespace Lumyte.Resources;

public sealed record ResourceSchedulingOptions
{
    public int MaxConcurrentLoads { get; init; } = Environment.ProcessorCount;

    public TimeSpan AgingInterval { get; init; } = TimeSpan.FromSeconds(1);

    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    public IDictionary<ResourceExecutionLane, int> MaxConcurrentLoadsPerLane { get; init; } =
        new Dictionary<ResourceExecutionLane, int>();
}
