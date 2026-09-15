using Lumyte.Graphics.RenderGraph.Conformance;

namespace Lumyte.Graphics.WebGPU.Browser.Tests;

[Collection("BrowserGpu")]
[Trait("Category", "WebGpuBrowserConformance")]
[Trait("Category", "TwoDConformance")]
public sealed class BrowserTwoDTests(BrowserGpuFixture fixture)
{
    [Theory]
    [InlineData("TwoDShapes", "shapes")]
    [InlineData("TwoDImage", "image-path-clip")]
    public async Task BrowserGraphMatchesTheSkiaReference(string browserCase, string scenario)
    {
        var result = await fixture.RunAsync(browserCase);
        byte[] pixels = Convert.FromBase64String(result.GetProperty("pixels").GetString()!);

        TwoDPixelComparison.AssertMatches("browser-" + scenario, pixels, result.GetProperty("rowPitch").GetInt32(),
            GpuFormat.Rgba8Unorm, SkiaTwoDReference.Render(TwoDScenarios.Create(scenario)));
    }

    [Fact]
    public async Task BrowserHalfFloatLayersPreserveHdrColor()
    {
        var result = await fixture.RunAsync("TwoDHdrLayer");
        byte[] pixels = Convert.FromBase64String(result.GetProperty("pixels").GetString()!);

        TwoDPixelComparison.AssertMatches("browser-hdr-layer", pixels, result.GetProperty("rowPitch").GetInt32(),
            GpuFormat.Rgba16Float, SkiaTwoDReference.RenderHalf(TwoDScenarios.Create("hdr-layer")), referenceFormat: GpuFormat.Rgba16Float);
    }
}
