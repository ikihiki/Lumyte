using System.Numerics;
using Lumyte.Graphics.Passes;
using Lumyte.Graphics.Tests;

namespace Lumyte.Graphics.DirectX12.Tests;

[Collection("GpuBackend")]
[Trait("Category", "DirectX12Conformance")]
public sealed class DirectX12FeatureGraphTests
{
    // Pixel conformance does not require the optional Windows Graphics Tools debug layer.
    // This machine reports 0x887A002D from D3D12GetDebugInterface when validation is requested.
    [Fact]
    public Task HostedConsumerClearsCopiesAndOutputsLinearPixels()
        => NativeFeatureGraphConformance.RunAsync("dx12", () => DirectX12Backend.Create(),
            GpuFormat.Rgba8Unorm, OutputEncoding.Linear, OutputAlphaMode.Opaque, new Vector4(0.25f, 0.5f, 0.75f, 1));
    [Fact]
    public Task HostedConsumerEncodesPremultipliedSrgbPixels()
        => NativeFeatureGraphConformance.RunAsync("dx12", () => DirectX12Backend.Create(),
            GpuFormat.Rgba8Unorm, OutputEncoding.Srgb, OutputAlphaMode.Premultiplied, new Vector4(0.8f, 0.4f, 0.2f, 0.5f));
    [Fact]
    public Task HostedConsumerAvoidsDoubleEncodingSrgbAttachment()
        => NativeFeatureGraphConformance.RunAsync("dx12", () => DirectX12Backend.Create(),
            GpuFormat.Rgba8UnormSrgb, OutputEncoding.Srgb, OutputAlphaMode.Premultiplied, new Vector4(0.8f, 0.4f, 0.2f, 0.5f));
    [Fact]
    public Task HostedPresentationAcquiresSubmitsAndPresentsTheCommonConsumer()
        => NativeFeatureGraphConformance.PresentAsync("dx12", () => DirectX12Backend.Create());
}
