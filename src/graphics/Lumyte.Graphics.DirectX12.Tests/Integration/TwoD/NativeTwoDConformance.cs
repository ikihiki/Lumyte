using System.Numerics;

using Lumyte.Graphics.Hosting;
using Lumyte.Graphics.Native;
using Lumyte.Graphics.Native.Hosting;
using Lumyte.Graphics.Native.RenderGraph;
using Lumyte.Graphics.Passes.Hosting;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.RenderGraph.Conformance;
using Lumyte.Graphics.TwoD;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Lumyte.Graphics.Tests;

internal static class NativeTwoDConformance
{
    internal static async Task CompareAsync(string backend, Func<INativeGpuBackend> create, string scenario, GpuFormat format = GpuFormat.Rgba8Unorm)
    {
        using IHost host = CreateHost(backend, create);
        await host.StartAsync();
        var runtime = (NativeRenderRuntime)(await host.Services.GetRequiredService<IGpuGraphicsSessionAccessor>().GetAsync()).Runtime;
        Draw2DScene scene = TwoDScenarios.Create(scenario);
        var fixture = TwoDRenderConsumer.CreatePlan(scene, format);
        using (var execution = await runtime.SubmitAsync(fixture.Plan))
        {
            await execution.WaitForCompletionAsync();
            var pixels = await ReadAsync(runtime, execution.GetExportedTexture(fixture.Output));
            bool hdr = format == GpuFormat.Rgba16Float;
            TwoDPixelComparison.AssertMatches($"{backend}-{scenario}-{format}", pixels, RowPitch(format), format,
                hdr ? SkiaTwoDReference.RenderHalf(scene) : SkiaTwoDReference.Render(scene),
                referenceFormat: hdr ? GpuFormat.Rgba16Float : GpuFormat.Rgba8Unorm);
        }
        await host.StopAsync();
    }

    internal static async Task RetainedSnapshotsAsync(string backend, Func<INativeGpuBackend> create)
    {
        using IHost host = CreateHost(backend, create);
        await host.StartAsync();
        var runtime = (NativeRenderRuntime)(await host.Services.GetRequiredService<IGpuGraphicsSessionAccessor>().GetAsync()).Runtime;
        var store = new Draw2DSceneStore();
        var node = store.CreateNode(TwoDScenarios.Create("shapes"));
        Draw2DScene firstScene = store.Snapshot();
        var fixture = TwoDRenderConsumer.CreatePlan(firstScene);
        var values = fixture.Plan.CreateBindings();
        store.SetTransform(node, Matrix3x2.CreateTranslation(4, -2));
        Draw2DScene secondScene = store.Snapshot();
        values.Set(fixture.Scene, secondScene);
        using (var first = await runtime.SubmitAsync(fixture.Plan))
        using (var second = await runtime.SubmitAsync(fixture.Plan, values.Build()))
        using (var replay = await runtime.SubmitAsync(fixture.Plan))
        {
            await first.WaitForCompletionAsync();
            await second.WaitForCompletionAsync();
            await replay.WaitForCompletionAsync();
            await Compare(first, firstScene, "original");
            await Compare(second, secondScene, "changed");
            await Compare(replay, firstScene, "replayed");
        }
        await host.StopAsync();

        async Task Compare(GpuRenderGraphExecution execution, Draw2DScene scene, string name)
        {
            var pixels = await ReadAsync(runtime, execution.GetExportedTexture(fixture.Output));
            TwoDPixelComparison.AssertMatches($"{backend}-{name}", pixels, RowPitch(GpuFormat.Rgba8Unorm), GpuFormat.Rgba8Unorm, SkiaTwoDReference.Render(scene));
        }
    }
    internal static async Task GraphTextureAsync(string backend, Func<INativeGpuBackend> create)
    {
        using IHost host = CreateHost(backend, create);
        await host.StartAsync();
        var runtime = (NativeRenderRuntime)(await host.Services.GetRequiredService<IGpuGraphicsSessionAccessor>().GetAsync()).Runtime;
        var fixture = TwoDRenderConsumer.CreateTexturePlan();
        using (var execution = await runtime.SubmitAsync(fixture.Plan.Plan))
        {
            await execution.WaitForCompletionAsync();
            var pixels = await ReadAsync(runtime, execution.GetExportedTexture(fixture.Plan.Output));
            TwoDPixelComparison.AssertMatches($"{backend}-graph-texture", pixels, RowPitch(GpuFormat.Rgba8Unorm),
                GpuFormat.Rgba8Unorm, SkiaTwoDReference.Render(fixture.Reference));
        }
        await host.StopAsync();
    }
    private static IHost CreateHost(string id, Func<INativeGpuBackend> create) => new HostBuilder().ConfigureServices(services =>
        services.AddLumyteGraphics(options => options.Runtime = new() { ProviderId = id })
            .AddNativeProvider(id, (_, _, _) => new(create())).AddImageProcessing().Add2DRendering()).Build();
    internal static int RowPitch(GpuFormat format) => (TwoDRenderConsumer.Size * TwoDPixelComparison.BytesPerPixel(format) + 255) / 256 * 256;
    private static Task<byte[]> ReadAsync(NativeRenderRuntime runtime, GpuGraphTextureRef texture)
    {
        const int size = TwoDRenderConsumer.Size;
        int pitch = RowPitch(texture.Description.Format), bpp = TwoDPixelComparison.BytesPerPixel(texture.Description.Format);
        return runtime.NativeResources.Manager.ReadTextureAsync(runtime.NativeResources.GetNativeTexture(texture),
            new(0, NativeGpuTextureAspect.Color, 0, 1, default, new(size, size, 1), (ulong)pitch, (ulong)(pitch * size)),
            (uint)size, (uint)(size * bpp), GpuTextureLayout.General, GpuTextureLayout.General,
            synchronization: new(GpuStage.All, GpuAccess.ColorWrite | GpuAccess.CopyWrite | GpuAccess.ShaderWrite));
    }
}
