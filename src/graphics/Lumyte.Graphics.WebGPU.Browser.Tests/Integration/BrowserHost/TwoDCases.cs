using Lumyte.Graphics;
using Lumyte.Graphics.Portable.Passes;
using Lumyte.Graphics.Portable.RenderGraph;
using Lumyte.Graphics.RenderGraph.Conformance;
using Lumyte.Graphics.WebGPU.Browser;

using P = Lumyte.Graphics.Portable;

public static partial class BrowserCases
{
    private static async Task<object> TwoDAsync(string scenario, GpuFormat format)
    {
        using WebGpuBrowserRuntime browser = await WebGpuBrowserRuntime.LoadAsync("/lumyte-webgpu.js");
        PortableRenderPassRegistry registry = new();
        registry.AddImageProcessing().Add2DRendering();
        var provider = new PortableRenderProvider("browser-2d", async (_, token) =>
        { token.ThrowIfCancellationRequested(); return await WebGpuBackend.CreateAsync(browser); }, registry);
        await using var runtime = (PortableRenderRuntime)await provider.CreateAsync(new());
        var scene = TwoDScenarios.Create(scenario);
        var fixture = TwoDRenderConsumer.CreatePlan(scene, format);
        using var execution = await runtime.SubmitAsync(fixture.Plan);
        await execution.WaitForCompletionAsync();
        var texture = execution.GetExportedTexture(fixture.Output);
        const int size = TwoDRenderConsumer.Size;
        int pitch = format == GpuFormat.Rgba16Float ? size * 8 : size * 4;
        byte[] pixels = await runtime.Resources.Manager.ReadTextureAsync(runtime.Resources.ResolveTexture(texture),
            new(0, P.GpuTextureAspect.All, default, new(size, size, 1), (ulong)pitch, (ulong)(pitch * size)));
        return new { pixels = Convert.ToBase64String(pixels), rowPitch = pitch };
    }
}
