using System.Numerics;
using Lumyte.Graphics.Hosting;
using Lumyte.Graphics.Passes;
using Lumyte.Graphics.Passes.Hosting;
using Lumyte.Graphics.Portable.Hosting;
using Lumyte.Graphics.Portable.RenderGraph;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.RenderGraph.Conformance;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using P = Lumyte.Graphics.Portable;

namespace Lumyte.Graphics.WebGPU.Tests;

[Collection("GpuBackend")]
[Trait("Category", "WebGpuPortableConformance")]
public sealed class WebGpuFeatureRenderGraphTests
{
    [Theory]
    [InlineData(OutputEncoding.Linear, OutputAlphaMode.Premultiplied, GpuFormat.Rgba8Unorm, 32, 64, 96, 128)]
    [InlineData(OutputEncoding.Srgb, OutputAlphaMode.Premultiplied, GpuFormat.Rgba8Unorm, 69, 94, 113, 128)]
    [InlineData(OutputEncoding.Srgb, OutputAlphaMode.Premultiplied, GpuFormat.Rgba8UnormSrgb, 69, 94, 113, 128)]
    public async Task HostedConsumerClearsCopiesAndOutputsWithoutBackendSpecificGraphCode(
        OutputEncoding encoding, OutputAlphaMode alphaMode, GpuFormat format, byte red, byte green, byte blue, byte alpha)
    {
        using IHost host = CreateHost();
        await host.StartAsync();
        IGpuRenderRuntime runtime = (await host.Services.GetRequiredService<IGpuGraphicsSessionAccessor>().GetAsync()).Runtime;
        var portable = Assert.IsType<PortableRenderRuntime>(runtime);
        ImagePipelinePlan consumer = ImagePipelineConsumer.CreateExportPlan(new(4, 4, format),
            TextureClearValue.Color(new Vector4(0.25f, 0.5f, 0.75f, 0.5f)), encoding, alphaMode);

        using (GpuRenderGraphExecution execution = await runtime.SubmitAsync(consumer.Plan))
        {
            await execution.WaitForCompletionAsync();
            GpuGraphTextureRef output = execution.GetExportedTexture(consumer.Output);
            byte[] bytes = await portable.Resources.Manager.ReadTextureAsync(portable.Resources.ResolveTexture(output),
                new(0, P.GpuTextureAspect.All, default, new(4, 4, 1), 256, 1024));

            AssertPixel(bytes.AsSpan(260, 4), [red, green, blue, alpha]);
        }
        runtime.Resources.Collect();
        Assert.Equal(0, portable.Resources.Manager.Statistics.ResourceCount);
        await host.StopAsync();
    }

    [Fact]
    public async Task SamePlanUsesNewInputValuesWhilePreviousExecutionKeepsItsOwnOutput()
    {
        using IHost host = CreateHost();
        await host.StartAsync();
        IGpuRenderRuntime runtime = (await host.Services.GetRequiredService<IGpuGraphicsSessionAccessor>().GetAsync()).Runtime;
        var portable = Assert.IsType<PortableRenderRuntime>(runtime);
        ImagePipelinePlan consumer = ImagePipelineConsumer.CreateExportPlan(new(4, 4, GpuFormat.Rgba8Unorm),
            TextureClearValue.Color(new Vector4(1, 0, 0, 1)), OutputEncoding.Linear, OutputAlphaMode.Opaque);
        GpuRenderGraphBindingsBuilder bindings = consumer.Plan.CreateBindings();
        bindings.Set(consumer.ColorInput, TextureClearValue.Color(new Vector4(0, 1, 0, 1)));
        using (GpuRenderGraphExecution first = await runtime.SubmitAsync(consumer.Plan))
        using (GpuRenderGraphExecution second = await runtime.SubmitAsync(consumer.Plan, bindings.Build()))
        {
            await first.WaitForCompletionAsync(); await second.WaitForCompletionAsync();
            foreach ((GpuRenderGraphExecution execution, byte[] expected) in new[] { (first, new byte[] { 255, 0, 0, 255 }), (second, new byte[] { 0, 255, 0, 255 }) })
            {
                byte[] bytes = await portable.Resources.Manager.ReadTextureAsync(
                    portable.Resources.ResolveTexture(execution.GetExportedTexture(consumer.Output)),
                    new(0, P.GpuTextureAspect.All, default, new(4, 4, 1), 256, 1024));
                AssertPixel(bytes.AsSpan(0, 4), expected);
            }
        }
        await host.StopAsync();
    }

    [Fact]
    public async Task HostedPresentationAcquiresSubmitsAndPresentsTheCommonConsumer()
    {
        using IHost host = CreateHost(presentation: true);
        await host.StartAsync();
        GpuGraphicsSession session = await host.Services.GetRequiredService<IGpuGraphicsSessionAccessor>().GetAsync();
        var runtime = Assert.IsType<PortableRenderRuntime>(session.Runtime);
        HeadlessImagePresentation presentation = host.Services.GetRequiredService<PresentationProbe>().Presentation!;
        ImagePresentationPlan consumer = ImagePipelineConsumer.CreatePresentationPlan(presentation.Texture.Description,
            TextureClearValue.Color(new Vector4(0.25f, 0.5f, 0.75f, 1)), OutputEncoding.Linear);

        using (GpuRenderGraphExecution execution = await session.RenderContext!.SubmitAsync(
            consumer.Plan, consumer.Plan.CreateBindings().Build(), consumer.TargetInput))
        {
            await execution.WaitForCompletionAsync();
            byte[] bytes = await runtime.Resources.Manager.ReadTextureAsync(runtime.Resources.ResolveTexture(presentation.Texture),
                new(0, P.GpuTextureAspect.All, default, new(4, 4, 1), 256, 1024));
            AssertPixel(bytes.AsSpan(0, 4), [64, 128, 191, 255]);
        }

        Assert.Equal((1, 1, 0), (presentation.AcquiredCount, presentation.PresentedCount, presentation.DiscardedCount));
        await host.StopAsync();
    }

    private static IHost CreateHost(bool presentation = false) => new HostBuilder().ConfigureServices(services =>
    {
        LumyteGraphicsBuilder builder = services.AddLumyteGraphics(options => options.Runtime = new() { ProviderId = "webgpu" })
            .AddPortableProvider("webgpu", static async (_, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return await WebGpuBackend.CreateAsync();
            }).AddImageProcessing();
        if (presentation) { services.AddSingleton<PresentationProbe>(); builder.UsePresentation<HeadlessPresentationFactory>(); }
    }).Build();

    private sealed class PresentationProbe { internal HeadlessImagePresentation? Presentation { get; set; } }
    private sealed class HeadlessPresentationFactory(PresentationProbe probe) : IGpuGraphicsPresentationFactory
    {
        public async ValueTask<IGpuGraphicsPresentationConnection> CreateAsync(IGpuRenderRuntime runtime, CancellationToken cancellationToken)
        {
            probe.Presentation = await HeadlessImagePresentation.CreateAsync(runtime, new(4, 4, GpuFormat.Rgba8Unorm), cancellationToken);
            return new PresentationConnection(probe.Presentation);
        }
    }
    private sealed class PresentationConnection(HeadlessImagePresentation presentation) : IGpuGraphicsPresentationConnection
    {
        public IGpuGraphPresentation Presentation => presentation;
        public ValueTask DisposeAsync() => presentation.DisposeAsync();
    }

    private static void AssertPixel(ReadOnlySpan<byte> actual, ReadOnlySpan<byte> expected)
    {
        string[] channels = ["red", "green", "blue", "alpha"];
        for (int index = 0; index < channels.Length; index++)
        { Assert.True(Math.Abs(actual[index] - expected[index]) <= 2, $"{channels[index]}: expected {expected[index]}, actual {actual[index]}"); }
    }
}
