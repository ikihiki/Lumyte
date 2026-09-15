using System.Text.Json;

using Lumyte.Graphics.RenderGraph.Conformance;

namespace Lumyte.Graphics.WebGPU.Browser.Tests;

[Collection("BrowserGpu")]
[Trait("Category", "WebGpuBrowserConformance")]
public sealed class BrowserPresentationTests(BrowserGpuFixture fixture)
{
    [Fact]
    [Trait("Category", "ImageFilterConformance")]
    public async Task StandardImageFiltersMatchReference()
    {
        var result = await fixture.RunAsync("ImageFilters");
        var cases = result.GetProperty("results").EnumerateArray().ToArray();
        Assert.Equal(ImageFilterConsumer.Cases, cases.Select(c => c.GetProperty("name").GetString()));
        foreach (var entry in cases)
        {
            var reference = ImageFilterConsumer.Create(entry.GetProperty("name").GetString()!);
            ImageFilterConsumer.Compare(reference, Convert.FromBase64String(entry.GetProperty("pixels").GetString()!), 256);
        }
    }
    [Fact]
    [Trait("Category", "WindowPresentation")]
    public async Task CanvasPixelsSurvivePresentationAndResize()
    {
        var result = await fixture.RunAsync("CanvasPresentation");
        var captures = result.GetProperty("captures").EnumerateArray().Select(c => JsonDocument.Parse(c.GetString()!)).ToArray();
        try
        {
            Assert.Equal([(32, 24), (48, 16), (24, 32)], captures.Select(c => (c.RootElement.GetProperty("width").GetInt32(), c.RootElement.GetProperty("height").GetInt32())));
            foreach (var capture in captures)
            {
                int[] pixels = capture.RootElement.GetProperty("pixels").EnumerateArray().Select(p => p.GetInt32()).ToArray();
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    int[] expected = [213, 188, 156, 255];
                    for (int channel = 0; channel < 4; channel++)
                    { Assert.True(Math.Abs(pixels[i + channel] - expected[channel]) <= 2, $"Canvas pixel {i / 4}, channel {channel}: expected {expected[channel]}, actual {pixels[i + channel]}."); }
                }
            }
        }
        finally { foreach (var capture in captures) { capture.Dispose(); } }
    }
}
