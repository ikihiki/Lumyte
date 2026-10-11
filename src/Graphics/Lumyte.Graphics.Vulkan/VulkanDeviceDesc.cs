namespace Lumyte.Graphics.Vulkan;

/// <summary>Configures device creation independently of surface creation.</summary>
public sealed record VulkanDeviceDesc
{
    /// <summary>Gets the zero-based physical device index.</summary>
    public uint PhysicalDeviceIndex { get; init; }

    /// <summary>Gets a value indicating whether equivalent graphics pipeline variants are reused.</summary>
    public bool CacheGraphicsPipelines { get; init; } = true;

    /// <summary>Gets additional instance extensions, including platform extensions needed by future surfaces.</summary>
    public IReadOnlyList<string> InstanceExtensions { get; init; } = [];

    /// <summary>Gets a value indicating whether surface and swapchain extensions and maintenance1 are enabled; creates no surface.</summary>
    public bool EnablePresentation { get; init; }
}
