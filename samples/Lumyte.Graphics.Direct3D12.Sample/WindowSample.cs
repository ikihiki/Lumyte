using System.Runtime.Versioning;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Samples;
using Lumyte.Platform.Windows;
using Lumyte.Windowing.Abstractions;

namespace Lumyte.Graphics.Direct3D12.Sample;

internal static class WindowSample
{
    internal static int Run(bool warp, bool partial)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            throw new PlatformNotSupportedException("The window sample requires Windows 10 version 1809 or later.");
        }

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
                {
                    RunWindow(warp, partial);
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null)
        {
            Console.Error.WriteLine(failure);
            return 1;
        }

        return 0;
    }

    [SupportedOSPlatform("windows10.0.17763")]
    private static void RunWindow(bool warp, bool partial)
    {
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateWindow(new() { Title = $"Lumyte DirectX 12 — {(warp ? "WARP" : "Hardware")}", ClientSize = new(960, 640) });
        using IDisposable retained = window.RetainNativeTarget();
        using var device = Direct3D12Device.Create(new() { UseWarp = warp, EnablePartialGraphicsPrograms = partial, EnableDebugLayer = true });
        using IGraphicsSurface surface = device.CreateSurface(((WindowsWindow)window).Hwnd);
        using IGraphicsSwapchain swapchain = surface.CreateSwapchain(new() { Width = (uint)window.State.PixelSize.Width, Height = (uint)window.State.PixelSize.Height, Format = TextureFormat.Bgra8Unorm });
        using IGraphicsShader vertex = device.CreateShader(ShaderArtifact.LoadEmbedded(typeof(WindowSample).Assembly, "Lumyte.Shaders.triangle-vertex.lshader"));
        using IGraphicsShader fragment = device.CreateShader(ShaderArtifact.LoadEmbedded(typeof(PipelineExercise).Assembly, "Lumyte.Shaders.color-fragment.lshader"));
        using IGraphicsPipeline pipeline = device.CreateGraphicsPipeline(new() { VertexShader = vertex, FragmentShader = fragment });
        window.Show();
        ulong frames = 0;
        while (!platform.IsQuitRequested && window.State.Lifetime == WindowLifetime.Open)
        {
            platform.PumpEvents();
            if (window.State.Lifetime != WindowLifetime.Open)
            {
                break;
            }

            WindowPixelSize size = window.State.PixelSize;
            if (size.Width <= 0 || size.Height <= 0)
            {
                Thread.Sleep(20);
                continue;
            }

            if (swapchain.Configuration.Width != size.Width || swapchain.Configuration.Height != size.Height)
            {
                swapchain.Reconfigure(swapchain.Configuration with { Width = (uint)size.Width, Height = (uint)size.Height });
            }

            DrawFrame(device, swapchain, pipeline);
            frames++;
            if (frames == 1 || frames % 300 == 0)
            {
                Console.WriteLine($"Presented {frames} DirectX 12 frames; SDK {device.SdkVersion}; {size.Width}x{size.Height}; partial={device.PartialGraphicsProgramsEnabled}.");
            }
        }
    }

    private static void DrawFrame(IGraphicDevice device, IGraphicsSwapchain swapchain, IGraphicsPipeline pipeline)
    {
        SurfaceAcquireResult acquired = swapchain.AcquireNextFrameAsync().AsTask().GetAwaiter().GetResult();
        using IGraphicsSurfaceFrame frame = acquired.Frame ?? throw new InvalidOperationException($"Acquire failed: {acquired.Status}");
        using IGraphicsTextureView view = frame.Texture.CreateView();
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        var color = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorWrite);
        commands.Barrier(new TextureBarrierDesc { Texture = frame.Texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = default, After = color });
        IRenderEncoder render = commands.BeginRenderPass(new() { ColorAttachments = [new() { View = view, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store, ClearValue = new(0.025, 0.04, 0.075, 1) }] });
        try
        {
            render.SetPipeline(pipeline);
            render.SetRenderState(new() { ColorTargets = [new()], Rasterization = new() { Cull = CullMode.None } });
            render.SetViewport(new(0, 0, frame.Texture.Width, frame.Texture.Height, 0, 1));
            render.SetScissor(new(0, 0, frame.Texture.Width, frame.Texture.Height));
            render.SetBlendConstant(new(0, 0, 0, 0));
            render.SetStencilReference(0);
            render.Draw(3);
        }
        finally
        {
            render.End();
        }

        commands.Barrier(new TextureBarrierDesc { Texture = frame.Texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.Present, Before = color, After = default });
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        SurfaceStatus presented = frame.Present();
        if (presented is not (SurfaceStatus.Success or SurfaceStatus.Suboptimal))
        {
            throw new InvalidOperationException($"Present failed: {presented}");
        }

        submission.WaitAsync().AsTask().GetAwaiter().GetResult();
        frame.WaitForReleaseAsync().AsTask().GetAwaiter().GetResult();
    }
}
