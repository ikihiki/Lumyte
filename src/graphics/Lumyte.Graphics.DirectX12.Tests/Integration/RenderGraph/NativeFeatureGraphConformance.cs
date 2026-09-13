using System.Numerics;
using Lumyte.Graphics.Hosting;
using Lumyte.Graphics.Native;
using Lumyte.Graphics.Native.Hosting;
using Lumyte.Graphics.Native.RenderGraph;
using Lumyte.Graphics.Passes;
using Lumyte.Graphics.Passes.Hosting;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.RenderGraph.Conformance;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Lumyte.Graphics.Tests;

internal static class NativeFeatureGraphConformance
{
    internal static async Task PresentAsync(string providerId, Func<INativeGpuBackend> factory)
    {
        using IHost host = new HostBuilder().ConfigureServices(services =>
        {
            services.AddSingleton<PresentationProbe>();
            services.AddLumyteGraphics(options => options.Runtime = new() { ProviderId = providerId })
                .AddNativeProvider(providerId, (_, _) => new(factory())).AddImageProcessing().UsePresentation<PresentationFactory>();
        }).Build();
        await host.StartAsync();
        GpuGraphicsSession session = await host.Services.GetRequiredService<IGpuGraphicsSessionAccessor>().GetAsync();
        NativeRenderRuntime runtime = (NativeRenderRuntime)session.Runtime;
        HeadlessImagePresentation presentation = host.Services.GetRequiredService<PresentationProbe>().Value!;
        var color = new Vector4(0.25f, 0.5f, 0.75f, 1);
        ImagePresentationPlan fixture = ImagePipelineConsumer.CreatePresentationPlan(presentation.Texture.Description,
            TextureClearValue.Color(color), OutputEncoding.Linear);

        using (GpuRenderGraphExecution execution = await session.RenderContext!.SubmitAsync(fixture.Plan,
            fixture.Plan.CreateBindings().Build(), fixture.TargetInput))
        {
            await execution.WaitForCompletionAsync();
            byte[] pixels = await runtime.NativeResources.Manager.ReadTextureAsync(runtime.NativeResources.GetNativeTexture(presentation.Texture),
                new(0, NativeGpuTextureAspect.Color, 0, 1, default, new(4, 4, 1), 256, 1024), 4, 16,
                GpuTextureLayout.General, GpuTextureLayout.General,
                synchronization: new(GpuStage.All, GpuAccess.ColorWrite | GpuAccess.CopyWrite | GpuAccess.ShaderWrite));
            AssertPixels(pixels, Expected(color, OutputEncoding.Linear, OutputAlphaMode.Opaque));
        }
        Assert.Equal((1, 1, 0), (presentation.AcquiredCount, presentation.PresentedCount, presentation.DiscardedCount));
        await host.StopAsync();
    }
    internal static async Task RunAsync(string providerId, Func<INativeGpuBackend> factory,
        GpuFormat format, OutputEncoding encoding, OutputAlphaMode alphaMode, Vector4 color)
    {
        using IHost host = new HostBuilder().ConfigureServices(services => services.AddLumyteGraphics(options => options.Runtime = new() { ProviderId = providerId })
            .AddNativeProvider(providerId, (_, _) => new(factory())).AddImageProcessing()).Build();
        await host.StartAsync();
        IGpuRenderRuntime runtime = (await host.Services.GetRequiredService<IGpuGraphicsSessionAccessor>().GetAsync()).Runtime;
        var native = (NativeRenderRuntime)runtime;
        ImagePipelinePlan fixture = ImagePipelineConsumer.CreateExportPlan(new(4, 4, format), TextureClearValue.Color(color), encoding, alphaMode);

        using (GpuRenderGraphExecution execution = await runtime.SubmitAsync(fixture.Plan))
        {
            await execution.WaitForCompletionAsync();
            GpuGraphTextureRef texture = execution.GetExportedTexture(fixture.Output);
            byte[] pixels = await native.NativeResources.Manager.ReadTextureAsync(native.NativeResources.GetNativeTexture(texture),
                new(0, NativeGpuTextureAspect.Color, 0, 1, default, new(4, 4, 1), 256, 1024), 4, 16,
                GpuTextureLayout.General, GpuTextureLayout.General,
                synchronization: new(GpuStage.All, GpuAccess.ColorWrite | GpuAccess.CopyWrite | GpuAccess.ShaderWrite));

            AssertPixels(pixels, Expected(color, encoding, alphaMode));
        }
        await runtime.WaitIdleAsync(); runtime.Resources.Collect();
        Assert.Equal(0, native.NativeResources.Manager.Statistics.ResourceCount);
        await host.StopAsync();
    }

    private static byte[] Expected(Vector4 color, OutputEncoding encoding, OutputAlphaMode alphaMode)
    {
        float alpha = Quantize(color.W) / 255f;
        float outputAlpha = alphaMode == OutputAlphaMode.Opaque ? 1 : alpha;
        return [Channel(color.X), Channel(color.Y), Channel(color.Z), Quantize(outputAlpha)];
        byte Channel(float value)
        {
            float premultiplied = Quantize(value * color.W) / 255f;
            float straight = alpha > 0 ? premultiplied / alpha : 0;
            float encoded = encoding == OutputEncoding.Linear ? straight
                : straight <= 0.0031308f ? 12.92f * straight : 1.055f * MathF.Pow(straight, 1 / 2.4f) - 0.055f;
            return Quantize(encoded * outputAlpha);
        }
        static byte Quantize(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255), 0, 255);
    }
    private static void AssertPixels(byte[] actual, byte[] expected)
    {
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                for (int channel = 0; channel < 4; channel++)
                {
                    int offset = row * 256 + column * 4 + channel;
                    Assert.True(Math.Abs(actual[offset] - expected[channel]) <= 1,
                        $"Pixel ({column}, {row}) channel {channel}: expected {expected[channel]}, actual {actual[offset]}.");
                }
            }
        }
    }
    private sealed class PresentationProbe { internal HeadlessImagePresentation? Value; }
    private sealed class PresentationFactory(PresentationProbe probe) : IGpuGraphicsPresentationFactory
    {
        public async ValueTask<IGpuGraphicsPresentationConnection> CreateAsync(IGpuRenderRuntime runtime, CancellationToken cancellationToken)
        {
            probe.Value = await HeadlessImagePresentation.CreateAsync(runtime, new(4, 4, GpuFormat.Rgba8Unorm), cancellationToken);
            return new Connection(probe.Value);
        }
    }
    private sealed class Connection(HeadlessImagePresentation presentation) : IGpuGraphicsPresentationConnection
    {
        public IGpuGraphPresentation Presentation => presentation;
        public ValueTask DisposeAsync() => presentation.DisposeAsync();
    }
}
