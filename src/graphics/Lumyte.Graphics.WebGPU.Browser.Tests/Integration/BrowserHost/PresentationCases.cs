using System.Numerics;
using System.Runtime.InteropServices.JavaScript;

using Lumyte.Graphics;
using Lumyte.Graphics.Passes;
using Lumyte.Graphics.Portable.Passes;
using Lumyte.Graphics.Portable.RenderGraph;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.RenderGraph.Conformance;
using Lumyte.Graphics.WebGPU.Browser;

using P = Lumyte.Graphics.Portable;

public static partial class BrowserCases
{
    [JSImport("captureCanvas", "Lumyte.PresentationTest")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>()]
    private static partial Task<string> CaptureCanvasAsync();
    private static async Task<object> CanvasPresentationAsync()
    {
        using var testModule = await JSHost.ImportAsync("Lumyte.PresentationTest", "/presentation-test.js");
        using var browser = await WebGpuBrowserRuntime.LoadAsync("/lumyte-webgpu.js");
        WebGpuBackend? backend = null;
        var provider = new PortableRenderProvider("canvas", async (_, token) => { token.ThrowIfCancellationRequested(); return backend = await WebGpuBackend.CreateAsync(browser); },
            new PortableRenderPassRegistry().AddImageProcessing());
        await using var runtime = (PortableRenderRuntime)await provider.CreateAsync(new());
        (uint Width, uint Height) size = (32, 24);
        await using var presentation = new PortableGraphPresentation(runtime.Resources, backend!.CreateCanvasSurface("presentation"), () => size);
        using var context = new GpuRenderContext(runtime, presentation);
        var captures = new List<string>();
        foreach (var extent in new[] { (32u, 24u), (48u, 16u), (24u, 32u) })
        {
            size = extent;
            using (var frame = await context.BeginFrameAsync())
            {
                var source = frame.Graph.CreateTexture("hdr", new(size.Width, size.Height, GpuFormat.Rgba16Float));
                frame.Graph.AddClearPass("clear", new(source, TextureClearValue.Color(new Vector4(2, 1, .5f, 1))));
                var tone = frame.Graph.AddToneMapPass("tone", new(source));
                frame.Graph.AddOutputPass("output", new(tone.Color, frame.TargetResource));
                using var execution = await frame.SubmitAsync();
                await execution.WaitForCompletionAsync();
            }
            await presentation.WaitForPresentationAsync();
            captures.Add(await CaptureCanvasAsync());
            using (var discard = await context.BeginFrameAsync())
            { }
            await presentation.WaitForPresentationAsync();
        }
        return new { captures };
    }
    private static async Task<object> ImageFiltersAsync()
    {
        using var browser = await WebGpuBrowserRuntime.LoadAsync("/lumyte-webgpu.js");
        var provider = new PortableRenderProvider("filters", async (_, token) => { token.ThrowIfCancellationRequested(); return await WebGpuBackend.CreateAsync(browser); },
            new PortableRenderPassRegistry().AddImageProcessing().Add2DRendering());
        await using var runtime = (PortableRenderRuntime)await provider.CreateAsync(new());
        var results = new List<object>();
        foreach (string name in ImageFilterConsumer.Cases)
        {
            var fixture = ImageFilterConsumer.Create(name);
            using var execution = await runtime.SubmitAsync(fixture.Plan);
            await execution.WaitForCompletionAsync();
            var texture = runtime.Resources.ResolveTexture(execution.GetExportedTexture(fixture.Output));
            byte[] pixels = await runtime.Resources.Manager.ReadTextureAsync(texture, new(0, P.GpuTextureAspect.All, default,
                new(fixture.Description.Width, fixture.Description.Height, 1), 256, 256 * fixture.Description.Height));
            results.Add(new { name, pixels = Convert.ToBase64String(pixels) });
        }
        return new { results };
    }
}
