namespace Lumyte.Pipelines;

internal sealed class CpuCapabilities : IStageCapabilities
{
    internal static CpuCapabilities Instance { get; } = new();
}
