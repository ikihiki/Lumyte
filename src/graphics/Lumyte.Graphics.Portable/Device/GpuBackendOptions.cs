namespace Lumyte.Graphics.Portable;

/// <summary>Creation requirements. Direct root input is mandatory and cannot be disabled.</summary>
public sealed record GpuBackendOptions
{
    public bool RequireDualSourceBlend { get; init; }
    public bool RequireIndirectFirstInstance { get; init; }
    public GpuRequiredLimits RequiredLimits { get; init; } = new();
}
