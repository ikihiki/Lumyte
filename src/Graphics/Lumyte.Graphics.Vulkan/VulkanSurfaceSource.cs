using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

/// <summary>Supplies platform extension names and a surface creation callback without obtaining window handles.</summary>
public sealed record VulkanSurfaceSource
{
    /// <summary>Gets the platform instance extensions required by the supplied native target.</summary>
    public required IReadOnlyList<string> InstanceExtensions { get; init; }

    /// <summary>Gets the callback creating a new surface for the supplied instance; ownership transfers to the backend.</summary>
    public required Func<Instance, SurfaceKHR> CreateSurface { get; init; }
}
