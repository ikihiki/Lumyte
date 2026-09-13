using System.Numerics;
using Lumyte.Graphics.Passes;
using Lumyte.Graphics.Tests;

namespace Lumyte.Graphics.DirectX12.Tests;

[Collection("GpuBackend")]
[Trait("Category", "DirectX12Conformance")]
[Trait("Category", "DirectX12Validation")]
public sealed class DirectX12FeatureGraphTests
{
    [Fact]
    public async Task ReusedPlanPreservesOverlappingOutputsWithTransientReuse()
    {
        using var validation = new DirectX12ValidationScope();

        await NativeFeatureGraphConformance.ReusePlanAsync("dx12", validation.CreateBackend);

        validation.AssertNoWarningsOrErrors();
    }

    // Requires Windows Graphics Tools. Every case checks the native debug layer as well as pixels.
    [Fact]
    public async Task HostedConsumerClearsCopiesAndOutputsLinearPixels()
    {
        using var validation = new DirectX12ValidationScope();

        await NativeFeatureGraphConformance.RunAsync("dx12", validation.CreateBackend,
            GpuFormat.Rgba8Unorm, OutputEncoding.Linear, OutputAlphaMode.Opaque, new Vector4(0.25f, 0.5f, 0.75f, 1));

        validation.AssertNoWarningsOrErrors();
    }
    [Fact]
    public async Task HostedConsumerEncodesPremultipliedSrgbPixels()
    {
        using var validation = new DirectX12ValidationScope();

        await NativeFeatureGraphConformance.RunAsync("dx12", validation.CreateBackend,
            GpuFormat.Rgba8Unorm, OutputEncoding.Srgb, OutputAlphaMode.Premultiplied, new Vector4(0.8f, 0.4f, 0.2f, 0.5f));

        validation.AssertNoWarningsOrErrors();
    }
    [Fact]
    public async Task HostedConsumerAvoidsDoubleEncodingSrgbAttachment()
    {
        using var validation = new DirectX12ValidationScope();

        await NativeFeatureGraphConformance.RunAsync("dx12", validation.CreateBackend,
            GpuFormat.Rgba8UnormSrgb, OutputEncoding.Srgb, OutputAlphaMode.Premultiplied, new Vector4(0.8f, 0.4f, 0.2f, 0.5f));

        validation.AssertNoWarningsOrErrors();
    }
    [Fact]
    public async Task HostedPresentationAcquiresSubmitsAndPresentsTheCommonConsumer()
    {
        using var validation = new DirectX12ValidationScope();

        await NativeFeatureGraphConformance.PresentAsync("dx12", validation.CreateBackend);

        validation.AssertNoWarningsOrErrors();
    }
}
