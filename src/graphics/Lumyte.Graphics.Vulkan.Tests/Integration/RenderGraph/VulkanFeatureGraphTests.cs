using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using Lumyte.Graphics.Passes;
using Lumyte.Graphics.Tests;

namespace Lumyte.Graphics.Vulkan.Tests;

[Collection("GpuBackend")]
[Trait("Category", "VulkanNativeConformance")]
[Trait("Category", "VulkanValidation")]
public sealed class VulkanFeatureGraphTests
{
    [VulkanNativeFact]
    public Task HostedConsumerClearsCopiesAndOutputsLinearPixels()
        => WithValidationAsync(() => NativeFeatureGraphConformance.RunAsync("vulkan", CreateBackend,
            GpuFormat.Rgba8Unorm, OutputEncoding.Linear, OutputAlphaMode.Opaque, new Vector4(0.25f, 0.5f, 0.75f, 1)));
    [VulkanNativeFact]
    public Task HostedConsumerEncodesPremultipliedSrgbPixels()
        => WithValidationAsync(() => NativeFeatureGraphConformance.RunAsync("vulkan", CreateBackend,
            GpuFormat.Rgba8Unorm, OutputEncoding.Srgb, OutputAlphaMode.Premultiplied, new Vector4(0.8f, 0.4f, 0.2f, 0.5f)));
    [VulkanNativeFact]
    public Task HostedConsumerAvoidsDoubleEncodingSrgbAttachment()
        => WithValidationAsync(() => NativeFeatureGraphConformance.RunAsync("vulkan", CreateBackend,
            GpuFormat.Rgba8UnormSrgb, OutputEncoding.Srgb, OutputAlphaMode.Premultiplied, new Vector4(0.8f, 0.4f, 0.2f, 0.5f)));
    [VulkanNativeFact]
    public Task HostedPresentationAcquiresSubmitsAndPresentsTheCommonConsumer()
        => WithValidationAsync(() => NativeFeatureGraphConformance.PresentAsync("vulkan", CreateBackend));

    private static VulkanBackend CreateBackend() => VulkanBackend.Create(new() { EnableValidation = true });

    private static async Task WithValidationAsync(Func<Task> run)
    {
        using var validation = new ValidationMessages();
        Trace.Listeners.Add(validation);
        try { await run(); }
        finally { Trace.Listeners.Remove(validation); }
        Assert.Empty(validation.Messages);
    }

    private sealed class ValidationMessages : TraceListener
    {
        internal ConcurrentQueue<string> Messages { get; } = new();
        public override void Write(string? message) { }
        public override void WriteLine(string? message) { }
        public override void WriteLine(string? message, string? category)
        {
            if (category == "Vulkan validation") { Messages.Enqueue(message ?? string.Empty); }
        }
    }
}
