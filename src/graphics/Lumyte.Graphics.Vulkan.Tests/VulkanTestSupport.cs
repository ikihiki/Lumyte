using Lumyte.Graphics.Tests;

namespace Lumyte.Graphics.Vulkan.Tests;

internal static class VulkanTestSupport
{
    private static readonly Lazy<Snapshot> support = GpuBackendTestGate.CreateProbe(Probe, VulkanGpuTestGate.MutexName);

    internal static string? UnavailableReason => support.Value.UnavailableReason;

    internal static string? Require(Func<Snapshot, bool> supported, string reason)
    {
        Snapshot value = support.Value;
        return value.UnavailableReason ?? (supported(value) ? null : reason);
    }

    private static Snapshot Probe()
    {
        try
        {
            using var backend = VulkanBackend.Create();
            return new(null, backend.CopyQueue is not null, backend.SupportsSeparateDepthStencilLayouts,
                backend.SupportsImageCubeArray, backend.SupportsSamplerAnisotropy,
                backend.Capabilities.MeshShaders, backend.Capabilities.AmplificationShaders);
        }
        catch (NotSupportedException exception) { return new(exception.Message); }
    }

    internal sealed record Snapshot(string? UnavailableReason, bool CopyQueue = false,
        bool SeparateDepthStencilLayouts = false, bool ImageCubeArray = false,
        bool SamplerAnisotropy = false, bool MeshShaders = false, bool AmplificationShaders = false);
}

// Attribute constructors run during discovery, before the GPU collection fixture.
// Probe once under its cross-process gate and retain only immutable capability data.
internal sealed class VulkanNativeFactAttribute : FactAttribute
{
    public VulkanNativeFactAttribute() => Skip = VulkanTestSupport.UnavailableReason;
}

internal sealed class VulkanNativeTheoryAttribute : TheoryAttribute
{
    public VulkanNativeTheoryAttribute() => Skip = VulkanTestSupport.UnavailableReason;
}

internal sealed class VulkanCopyFactAttribute : FactAttribute
{
    public VulkanCopyFactAttribute() => Skip = VulkanTestSupport.Require(static value => value.CopyQueue,
        "No distinct Vulkan queue is available for asynchronous copies.");
}

internal sealed class VulkanSeparateDepthStencilFactAttribute : FactAttribute
{
    public VulkanSeparateDepthStencilFactAttribute() => Skip = VulkanTestSupport.Require(
        static value => value.SeparateDepthStencilLayouts,
        "Vulkan device requires combined depth/stencil layout initialization.");
}

internal sealed class VulkanCubeArrayDescriptorFactAttribute : FactAttribute
{
    public VulkanCubeArrayDescriptorFactAttribute() => Skip = VulkanTestSupport.Require(
        static value => value.ImageCubeArray, "Vulkan imageCubeArray is unavailable.");
}

internal sealed class VulkanAnisotropicDescriptorFactAttribute : FactAttribute
{
    public VulkanAnisotropicDescriptorFactAttribute() => Skip = VulkanTestSupport.Require(
        static value => value.SamplerAnisotropy, "Vulkan samplerAnisotropy is unavailable.");
}

internal sealed class VulkanMeshFactAttribute : FactAttribute
{
    public VulkanMeshFactAttribute(bool amplification = false) => Skip = VulkanMeshSupport.UnavailableReason(amplification);
}

internal sealed class VulkanMeshTheoryAttribute : TheoryAttribute
{
    public VulkanMeshTheoryAttribute(bool amplification = false) => Skip = VulkanMeshSupport.UnavailableReason(amplification);
}

internal static class VulkanMeshSupport
{
    public static string? UnavailableReason(bool amplification) =>
        VulkanTestSupport.Require(static value => value.MeshShaders, "Vulkan VK_EXT_mesh_shader meshShader is unavailable.")
        ?? (amplification ? VulkanTestSupport.Require(static value => value.AmplificationShaders,
            "Vulkan taskShader is unavailable.") : null);
}
