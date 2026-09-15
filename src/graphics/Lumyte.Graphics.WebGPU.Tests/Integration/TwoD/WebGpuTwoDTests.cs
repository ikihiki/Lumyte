using System.Numerics;

using Lumyte.Graphics.Hosting;
using Lumyte.Graphics.Passes.Hosting;
using Lumyte.Graphics.Passes;
using Lumyte.Graphics.Portable.Hosting;
using Lumyte.Graphics.Portable.RenderGraph;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.RenderGraph.Conformance;
using Lumyte.Graphics.TwoD;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using P = Lumyte.Graphics.Portable;

namespace Lumyte.Graphics.WebGPU.Tests;

[Collection("GpuBackend")]
[Trait("Category", "WebGpuPortableConformance")]
[Trait("Category", "TwoDConformance")]
public sealed class WebGpuTwoDTests
{
    [Fact]
    [Trait("Category", "WindowPresentation")]
    public Task PresentsAndResizesARealWindow() => WindowConformance.RunAsync(window =>
    {
        WebGpuBackend? backend = null;
        using IHost host = new HostBuilder().ConfigureServices(services =>
            services.AddLumyteGraphics(options => options.Runtime = new() { ProviderId = "window" })
                .AddPortableProvider("window", async (_, _, token) => { token.ThrowIfCancellationRequested(); return backend = await WebGpuBackend.CreateAsync(); })
                .AddImageProcessing()).Build();
        window.Complete(host.StartAsync());
        var session = window.Complete(host.Services.GetRequiredService<IGpuGraphicsSessionAccessor>().GetAsync().AsTask());
        var runtime = (PortableRenderRuntime)session.Runtime;
        var presentation = new PortableGraphPresentation(runtime.Resources, backend!.CreateWindowSurface(window.Handle), window.Size);
        try
        {
            using var context = new GpuRenderContext(runtime, presentation);
            foreach (var size in new[] { (Width: 160, Height: 120), (Width: 224, Height: 144), (Width: 128, Height: 96) })
            {
                window.Resize(size.Width, size.Height);
                using (var frame = window.Complete(context.BeginFrameAsync().AsTask()))
                {
                    Assert.Equal((uint)size.Width, frame.TargetResource.Description.Width);
                    Assert.Equal((uint)size.Height, frame.TargetResource.Description.Height);
                    var source = frame.Graph.CreateTexture("hdr", new((uint)size.Width, (uint)size.Height, GpuFormat.Rgba16Float));
                    frame.Graph.AddClearPass("clear", new(source, TextureClearValue.Color(new(2, 1, .5f, 1))));
                    var tone = frame.Graph.AddToneMapPass("tone", new(source));
                    frame.Graph.AddOutputPass("screen", new(tone.Color, frame.TargetResource));
                    using var execution = window.Complete(frame.SubmitAsync().AsTask());
                    window.Complete(execution.WaitForCompletionAsync().AsTask());
                }
                window.Complete(presentation.WaitForPresentationAsync());
                using (var discard = window.Complete(context.BeginFrameAsync().AsTask()))
                { }
                window.Complete(presentation.WaitForPresentationAsync());
            }
        }
        finally { window.Complete(presentation.DisposeAsync().AsTask()); window.Complete(host.StopAsync()); }
    });
    public static IEnumerable<object[]> FilterCases => ImageFilterConsumer.Cases.Select(name => new object[] { name });
    [Theory]
    [MemberData(nameof(FilterCases))]
    [Trait("Category", "ImageFilterConformance")]
    public async Task StandardImageFiltersMatchReference(string scenario)
    {
        using IHost host = CreateHost();
        await host.StartAsync();
        var runtime = (PortableRenderRuntime)(await host.Services.GetRequiredService<IGpuGraphicsSessionAccessor>().GetAsync()).Runtime;
        var fixture = ImageFilterConsumer.Create(scenario);
        using (var execution = await runtime.SubmitAsync(fixture.Plan))
        {
            await execution.WaitForCompletionAsync();
            var texture = runtime.Resources.ResolveTexture(execution.GetExportedTexture(fixture.Output));
            var pixels = await runtime.Resources.Manager.ReadTextureAsync(texture,
                new(0, P.GpuTextureAspect.All, default, new(fixture.Description.Width, fixture.Description.Height, 1), 256, 256 * fixture.Description.Height));
            ImageFilterConsumer.Compare(fixture, pixels, 256);
        }
        await host.StopAsync();
    }
    public static IEnumerable<object[]> Cases => TwoDScenarios.Names.Select(name => new object[] { name });
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task MatchesSkiaReference(string scenario)
    {
        using IHost host = CreateHost();
        await host.StartAsync();
        var runtime = (PortableRenderRuntime)(await host.Services.GetRequiredService<IGpuGraphicsSessionAccessor>().GetAsync()).Runtime;
        Draw2DScene scene = TwoDScenarios.Create(scenario);
        var fixture = TwoDRenderConsumer.CreatePlan(scene);
        using (var execution = await runtime.SubmitAsync(fixture.Plan))
        {
            await execution.WaitForCompletionAsync();
            byte[] pixels = await ReadAsync(runtime, execution.GetExportedTexture(fixture.Output));
            TwoDPixelComparison.AssertMatches($"webgpu-{scenario}", pixels, 256, GpuFormat.Rgba8Unorm, SkiaTwoDReference.Render(scene));
        }
        await host.StopAsync();
    }
    [Theory]
    [InlineData(GpuFormat.Bgra8Unorm)]
    [InlineData(GpuFormat.Rgba16Float)]
    public async Task TargetFormatsPreserveLinearColor(GpuFormat format)
    {
        using IHost host = CreateHost();
        await host.StartAsync();
        var runtime = (PortableRenderRuntime)(await host.Services.GetRequiredService<IGpuGraphicsSessionAccessor>().GetAsync()).Runtime;
        var scene = TwoDScenarios.Create("shapes");
        var fixture = TwoDRenderConsumer.CreatePlan(scene, format);
        using (var execution = await runtime.SubmitAsync(fixture.Plan))
        {
            await execution.WaitForCompletionAsync();
            byte[] pixels = await ReadAsync(runtime, execution.GetExportedTexture(fixture.Output));
            TwoDPixelComparison.AssertMatches($"webgpu-{format}", pixels, RowPitch(format), format, SkiaTwoDReference.Render(scene));
        }
        await host.StopAsync();
    }
    [Fact]
    public async Task HalfFloatLayersPreserveHdrColor()
    {
        using IHost host = CreateHost();
        await host.StartAsync();
        var runtime = (PortableRenderRuntime)(await host.Services.GetRequiredService<IGpuGraphicsSessionAccessor>().GetAsync()).Runtime;
        var scene = TwoDScenarios.Create("hdr-layer");
        var fixture = TwoDRenderConsumer.CreatePlan(scene, GpuFormat.Rgba16Float);
        using (var execution = await runtime.SubmitAsync(fixture.Plan))
        {
            await execution.WaitForCompletionAsync();
            byte[] pixels = await ReadAsync(runtime, execution.GetExportedTexture(fixture.Output));
            TwoDPixelComparison.AssertMatches("webgpu-hdr-layer", pixels, RowPitch(GpuFormat.Rgba16Float), GpuFormat.Rgba16Float,
                SkiaTwoDReference.RenderHalf(scene), referenceFormat: GpuFormat.Rgba16Float);
        }
        await host.StopAsync();
    }
    [Fact]
    public async Task SamplesATextureProducedByAnEarlierGraphPass()
    {
        using IHost host = CreateHost();
        await host.StartAsync();
        var runtime = (PortableRenderRuntime)(await host.Services.GetRequiredService<IGpuGraphicsSessionAccessor>().GetAsync()).Runtime;
        var fixture = TwoDRenderConsumer.CreateTexturePlan();
        using (var execution = await runtime.SubmitAsync(fixture.Plan.Plan))
        {
            await execution.WaitForCompletionAsync();
            byte[] pixels = await ReadAsync(runtime, execution.GetExportedTexture(fixture.Plan.Output));
            TwoDPixelComparison.AssertMatches("webgpu-graph-texture", pixels, 256, GpuFormat.Rgba8Unorm, SkiaTwoDReference.Render(fixture.Reference));
        }
        await host.StopAsync();
    }
    [Fact]
    public async Task RetainedSnapshotsRemainIndependentAcrossSubmissions()
    {
        using IHost host = CreateHost();
        await host.StartAsync();
        var runtime = (PortableRenderRuntime)(await host.Services.GetRequiredService<IGpuGraphicsSessionAccessor>().GetAsync()).Runtime;
        var store = new Draw2DSceneStore();
        var node = store.CreateNode(TwoDScenarios.Create("shapes"));
        var original = store.Snapshot();
        var fixture = TwoDRenderConsumer.CreatePlan(original);
        var values = fixture.Plan.CreateBindings();
        store.SetTransform(node, Matrix3x2.CreateTranslation(4, -2));
        var changed = store.Snapshot();
        values.Set(fixture.Scene, changed);
        using (var first = await runtime.SubmitAsync(fixture.Plan))
        using (var second = await runtime.SubmitAsync(fixture.Plan, values.Build()))
        using (var replay = await runtime.SubmitAsync(fixture.Plan))
        {
            await first.WaitForCompletionAsync();
            await second.WaitForCompletionAsync();
            await replay.WaitForCompletionAsync();
            await Compare(first, original, "original");
            await Compare(second, changed, "changed");
            await Compare(replay, original, "replayed");
        }
        await host.StopAsync();
        async Task Compare(GpuRenderGraphExecution execution, Draw2DScene scene, string name)
        {
            var pixels = await ReadAsync(runtime, execution.GetExportedTexture(fixture.Output));
            TwoDPixelComparison.AssertMatches($"webgpu-{name}", pixels, 256, GpuFormat.Rgba8Unorm, SkiaTwoDReference.Render(scene));
        }
    }
    private static IHost CreateHost() => new HostBuilder().ConfigureServices(services =>
        services.AddLumyteGraphics(options => options.Runtime = new() { ProviderId = "webgpu" })
            .AddPortableProvider("webgpu", static async (_, _, token) =>
            { token.ThrowIfCancellationRequested(); return await WebGpuBackend.CreateAsync(); })
            .AddImageProcessing().Add2DRendering()).Build();
    private static int RowPitch(GpuFormat format) => (TwoDRenderConsumer.Size * TwoDPixelComparison.BytesPerPixel(format) + 255) / 256 * 256;
    private static Task<byte[]> ReadAsync(PortableRenderRuntime runtime, GpuGraphTextureRef texture)
    {
        const int size = TwoDRenderConsumer.Size;
        int pitch = RowPitch(texture.Description.Format);
        return runtime.Resources.Manager.ReadTextureAsync(runtime.Resources.ResolveTexture(texture),
            new(0, P.GpuTextureAspect.All, default, new(size, size, 1), (ulong)pitch, (ulong)(pitch * size))).AsTask();
    }
}
