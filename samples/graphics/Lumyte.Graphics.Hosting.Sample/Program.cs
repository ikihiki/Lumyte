using System.Numerics;
using System.Runtime.InteropServices;

using Lumyte.Graphics;
using Lumyte.Graphics.DirectX12;
using Lumyte.Graphics.Hosting;
using Lumyte.Graphics.Native;
using Lumyte.Graphics.Native.Hosting;
using Lumyte.Graphics.Native.RenderGraph;
using Lumyte.Graphics.Passes;
using Lumyte.Graphics.Passes.Hosting;
using Lumyte.Graphics.Portable.Hosting;
using Lumyte.Graphics.Portable.RenderGraph;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.TwoD;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.WebGPU;
using Lumyte.Platform.Windows;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

internal static partial class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        string backendName = args.FirstOrDefault() ?? "dx12";
        if (backendName is not ("dx12" or "vulkan" or "webgpu"))
        { throw new ArgumentException("Choose dx12, vulkan or webgpu."); }
        int frameLimit = args.Length > 1 ? int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : int.MaxValue;
        using var platform = new WindowsPlatform();
        using var window = platform.CreateWindow(new() { Title = $"Lumyte — {backendName}: 2D / Blur / Composite / ToneMap", ClientSize = new(800, 480) });
        using var stop = new CancellationTokenSource();
        window.CloseRequested += (_, _) => stop.Cancel();
        bool models = args.Length > 2 && args[2] == "model";
        Task rendering = Task.Run(() => RenderAsync(backendName, window, frameLimit, models, stop.Token));
        while (!rendering.IsCompleted)
        {
            if (!platform.PumpEvents())
            { stop.Cancel(); }
            _ = MsgWaitForMultipleObjectsEx(0, 0, 10, 0x04FF, 0x04);
        }
        rendering.GetAwaiter().GetResult();
        // The Host and all surface use have ended before window disposal on its owning thread.
    }
    private static async Task RenderAsync(string backendName, WindowsWindow window, int frameLimit, bool models, CancellationToken stop)
    {
        INativeGpuBackend? native = null;
        WebGpuBackend? portable = null;
        GpuSurfacePresentation? presentation = null;
        using IHost host = new HostBuilder().ConfigureServices(services =>
        {
            var graphics = services.AddLumyteGraphics(options => options.Runtime = new() { ProviderId = backendName });
            if (backendName == "webgpu")
            { graphics.AddPortableProvider(backendName, async (_, _, _) => portable = await WebGpuBackend.CreateAsync()); }
            else
            { graphics.AddNativeProvider(backendName, (_, _, _) => new(native = backendName == "dx12" ? DirectX12Backend.Create() : VulkanBackend.Create())); }
            graphics.AddImageProcessing().Add2DRendering().AddModelRendering().UsePresentation((_, runtime, _) =>
            {
                presentation = runtime is NativeRenderRuntime n
                    ? new NativeGraphPresentation(n.NativeResources, native is DirectX12Backend dx ? dx.CreateWindowSurface(window.Handle) : ((VulkanBackend)native!).CreateWindowSurface(window.Handle), Size)
                    : new PortableGraphPresentation(((PortableRenderRuntime)runtime).Resources, portable!.CreateWindowSurface(window.Handle), Size);
                return new(new GpuGraphicsSurfaceConnection(presentation));
            });
        }).Build();
        try
        {
            await host.StartAsync(stop);
            var session = await host.Services.GetRequiredService<IGpuGraphicsSessionAccessor>().GetAsync(stop);
            if (models)
            { await RenderModelsAsync(session.RenderContext!,presentation!,frameLimit,stop); return; }
            using var builder = new Draw2DSceneBuilder();
            builder.FillRoundedRectangle(new(24, 24, 160, 128), 18, Brush.LinearGradient(new(24, 24), new(184, 152), new Color(4, .2f, .1f), new Color(.1f, .4f, 3)));
            builder.FillEllipse(new(200, 44, 88, 88), Brush.Solid(new(.1f, 3, 1, .85f)));
            Draw2DScene scene = builder.Finish();
            for (int frameNumber = 0; frameNumber < frameLimit && !stop.IsCancellationRequested; frameNumber++)
            {
                while (Size() is { Width: 0 } or { Height: 0 })
                { await Task.Delay(50, stop); }
                using var frame = await session.RenderContext!.BeginFrameAsync(stop);
                var graph = frame.Graph;
                var source = graph.CreateTexture("scene", new(320, 180, GpuFormat.Rgba16Float));
                graph.AddClearPass("background", new(source, TextureClearValue.Color(new(.02f, .03f, .06f, 1))));
                graph.Add2DPass("2d", new(scene, source));
                var blur = graph.AddBlurPass("blur", new(source, 3));
                graph.AddCompositePass("soft overlay", new(source, [new(blur.Color, .25f)]));
                var scaled = graph.CreateTexture("scaled", new(frame.TargetResource.Description.Width, frame.TargetResource.Description.Height, GpuFormat.Rgba16Float));
                graph.AddBlitPass("resize", new(source, scaled));
                var mapped = graph.AddToneMapPass("tone map", new(scaled));
                graph.AddOutputPass("screen", new(mapped.Color, frame.TargetResource));
                using var execution = await frame.SubmitAsync(stop);
                await execution.WaitForCompletionAsync();
                await presentation!.WaitForPresentationAsync();
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        finally { await host.StopAsync(); }
        (uint Width, uint Height) Size()
        { var size = window.FramebufferSize; return ((uint)size.Width, (uint)size.Height); }
    }
    [LibraryImport("user32.dll")]
    private static partial uint MsgWaitForMultipleObjectsEx(uint count, nint handles, uint milliseconds, uint mask, uint flags);
}
