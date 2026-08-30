namespace Lumyte.Resources;

public sealed record ResourceSchedulingOptions
{
    public int MaxConcurrentLoads { get; init; } = Environment.ProcessorCount;

    public IDictionary<ResourceExecutionLane, int> MaxConcurrentLoadsPerLane { get; init; } =
        new Dictionary<ResourceExecutionLane, int>();
}
