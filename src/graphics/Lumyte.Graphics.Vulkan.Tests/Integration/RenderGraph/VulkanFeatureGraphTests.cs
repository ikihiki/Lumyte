using System.Numerics;
using Lumyte.Graphics.Passes;
using Lumyte.Graphics.Tests;

namespace Lumyte.Graphics.Vulkan.Tests;

[Collection("GpuBackend")]
[Trait("Category", "VulkanNativeConformance")]
public sealed class VulkanFeatureGraphTests
{
    // This machine does not provide VK_LAYER_KHRONOS_validation; pixel conformance uses the normal device.
    [VulkanNativeFact]
    public Task HostedConsumerClearsCopiesAndOutputsLinearPixels()
        => NativeFeatureGraphConformance.RunAsync("vulkan", () => VulkanBackend.Create(),
            GpuFormat.Rgba8Unorm, OutputEncoding.Linear, OutputAlphaMode.Opaque, new Vector4(0.25f, 0.5f, 0.75f, 1));
    [VulkanNativeFact]
    public Task HostedConsumerEncodesPremultipliedSrgbPixels()
        => NativeFeatureGraphConformance.RunAsync("vulkan", () => VulkanBackend.Create(),
            GpuFormat.Rgba8Unorm, OutputEncoding.Srgb, OutputAlphaMode.Premultiplied, new Vector4(0.8f, 0.4f, 0.2f, 0.5f));
    [VulkanNativeFact]
    public Task HostedConsumerAvoidsDoubleEncodingSrgbAttachment()
        => NativeFeatureGraphConformance.RunAsync("vulkan", () => VulkanBackend.Create(),
            GpuFormat.Rgba8UnormSrgb, OutputEncoding.Srgb, OutputAlphaMode.Premultiplied, new Vector4(0.8f, 0.4f, 0.2f, 0.5f));
    [VulkanNativeFact]
    public Task HostedPresentationAcquiresSubmitsAndPresentsTheCommonConsumer()
        => NativeFeatureGraphConformance.PresentAsync("vulkan", () => VulkanBackend.Create());
}
