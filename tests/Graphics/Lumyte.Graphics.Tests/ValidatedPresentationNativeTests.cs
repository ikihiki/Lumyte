using System.Diagnostics;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Exercises actual X11 acquire, clear, submit, present and retirement with paired diagnostics.</summary>
public sealed class ValidatedPresentationNativeTests
{
    /// <summary>Checks the native Vulkan presentation ownership and synchronization chain.</summary>
    /// <param name="enabled">Whether optional managed diagnostics are enabled.</param>
    /// <returns>The native frame completion.</returns>
    [PresentationTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VulkanClearAndPresentAsync(bool enabled)
    {
        using var window = new X11Window();
        using var device = VulkanDevice.Create(new VulkanDeviceDesc
        {
            EnablePresentation = true,
            InstanceExtensions = ["VK_KHR_xlib_surface"],
        });
        if (enabled)
        {
            device.EnableValidation();
        }

        Assert.Equal(enabled, device.Validation != null);
        using IGraphicsSurface surface = device.CreateSurface(window.CreateVulkanSurface);
        await ClearAndPresentAsync(device, surface);
    }

    /// <summary>Checks the native WebGPU presentation ownership and single-queue ordering chain.</summary>
    /// <param name="enabled">Whether optional managed diagnostics are enabled.</param>
    /// <returns>The native frame completion.</returns>
    [PresentationTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WgpuClearAndPresentAsync(bool enabled)
    {
        using var window = new X11Window();
        using var device = WgpuDevice.Create();
        if (enabled)
        {
            device.EnableValidation();
        }

        Assert.Equal(enabled, device.Validation != null);
        using IGraphicsSurface surface = device.CreateSurface(Ahjo.Wgpu.SurfaceSource.XlibWindow(window.Display, (ulong)window.Window));
        await ClearAndPresentAsync(device, surface);
    }

    private static async Task ClearAndPresentAsync(IGraphicDevice device, IGraphicsSurface surface)
    {
        Lumyte.Graphics.Abstractions.SurfaceCapabilities caps = surface.GetCapabilities();
        Assert.NotEmpty(caps.Formats);
        Assert.Contains(PresentMode.Fifo, caps.PresentModes);
        Assert.True((caps.SupportedUsage & TextureUsage.RenderAttachment) != 0);
        using IGraphicsSwapchain swapchain = surface.CreateSwapchain(new SwapchainDesc
        {
            Width = caps.CurrentWidth ?? X11Window.Size,
            Height = caps.CurrentHeight ?? X11Window.Size,
            Format = caps.Formats[0],
            Usage = TextureUsage.RenderAttachment,
            PresentMode = PresentMode.Fifo,
        });
        using IGraphicsSemaphore available = device.CreateSemaphore();
        using IGraphicsSemaphore rendered = device.CreateSemaphore();
        using IGraphicsSurfaceFrame frame = await AcquireAsync(swapchain, available);
        Assert.Equal(SurfaceFrameStatus.Acquired, frame.Status);
        using IGraphicsTextureView view = frame.Texture.CreateView();
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new CommandBufferDesc { Label = "Native presentation clear" });
        var colorWrite = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorWrite);
        commands.Barrier(new TextureBarrierDesc
        {
            Texture = frame.Texture,
            Range = new(0, 1, 0, 1),
            BeforeState = TextureState.Undefined,
            AfterState = TextureState.ColorAttachment,
            Before = default,
            After = colorWrite,
        });
        IRenderEncoder pass = commands.BeginRenderPass(new RenderPassDesc
        {
            ColorAttachments =
            [
                new RenderColorAttachmentDesc
                {
                    View = view,
                    LoadOp = Lumyte.Graphics.Abstractions.AttachmentLoadOp.Clear,
                    StoreOp = Lumyte.Graphics.Abstractions.AttachmentStoreOp.Store,
                    ClearValue = new(0.125, 0.25, 0.5, 1),
                },
            ],
        });
        pass.End();
        commands.Barrier(new TextureBarrierDesc
        {
            Texture = frame.Texture,
            Range = new(0, 1, 0, 1),
            BeforeState = TextureState.ColorAttachment,
            AfterState = TextureState.Present,
            Before = colorWrite,
            After = default,
        });
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit(new QueueSubmitDesc
        {
            CommandBuffers = [commands],
            WaitSemaphores = [new() { Semaphore = available, Stages = PipelineStage.ColorOutput }],
            SignalSemaphores = [rendered],
        });

        // Always retire submitted native work, even when the presentation assertion fails.

        // Cancellation must not unwind using scopes while the GPU still owns their resources.
        try
        {
            SurfaceStatus presented = frame.Present([rendered]);
            Assert.True(presented is SurfaceStatus.Success or SurfaceStatus.Suboptimal, $"Present returned {presented}.");
            Assert.Equal(SurfaceFrameStatus.Presented, frame.Status);
        }
        finally
        {
            await submission.WaitAsync();
            await frame.WaitForReleaseAsync();
        }

        Assert.Equal(SubmissionStatus.Completed, submission.Status);
        Assert.Equal(GraphicsDeviceStatus.Ready, device.Status);

        // Reverse using order releases submission/commands, view, frame, semaphores,

        // and swapchain; the caller then releases surface, device and the X11 target.
    }

    private static async Task<IGraphicsSurfaceFrame> AcquireAsync(IGraphicsSwapchain swapchain, IGraphicsSemaphore available)
    {
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            SurfaceAcquireResult result = await swapchain.AcquireNextFrameAsync(available);
            if (result.Status is SurfaceStatus.Success or SurfaceStatus.Suboptimal)
            {
                Assert.NotNull(result.Frame);
                return result.Frame;
            }

            Assert.Null(result.Frame);
            Assert.True(result.Status == SurfaceStatus.Timeout, $"Acquire returned {result.Status}.");
            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(10), "No presentation image became available within ten seconds.");
            await Task.Delay(10);
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    private sealed class PresentationTheoryAttribute : TheoryAttribute
    {
        public PresentationTheoryAttribute()
        {
            if (!OperatingSystem.IsLinux())
            {
                Skip = "Native presentation fixture requires Linux X11.";
            }
            else if (Environment.GetEnvironmentVariable("LUMYTE_GRAPHICS_PRESENTATION_TESTS") != "1")
            {
                Skip = "Set LUMYTE_GRAPHICS_PRESENTATION_TESTS=1 to opt in to native window presentation.";
            }
            else if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")))
            {
                Skip = "Allocate an X11 display and set DISPLAY before running presentation tests.";
            }
        }
    }

    private sealed class X11Window : IDisposable
    {
        public const uint Size = 64;

        public X11Window()
        {
            // These tests are serialized by the assembly's GPU collection policy.
            // Initialize Xlib threading before opening this fixture's connection;
            // the driver may also access that connection on its own threads.
            if (XInitThreads() == 0)
            {
                throw new InvalidOperationException("XInitThreads failed.");
            }

            Display = XOpenDisplay(0);
            if (Display == 0)
            {
                throw new InvalidOperationException("Cannot open DISPLAY; check X11 server availability and authorization.");
            }

            Window = XCreateSimpleWindow(Display, XDefaultRootWindow(Display), 0, 0, Size, Size, 0, 0, 0);
            if (Window == 0)
            {
                XCloseDisplay(Display);
                throw new InvalidOperationException("XCreateSimpleWindow failed.");
            }

            XMapWindow(Display, Window);
            XSync(Display, 0);
        }

        public nint Display { get; }

        public nuint Window { get; }

        public unsafe SurfaceKHR CreateVulkanSurface(Instance instance)
        {
            using var api = Vk.GetApi();
            if (!api.TryGetInstanceExtension<KhrXlibSurface>(instance, out KhrXlibSurface xlib))
            {
                throw new NotSupportedException("VK_KHR_xlib_surface is unavailable.");
            }

            using (xlib)
            {
                var info = new XlibSurfaceCreateInfoKHR
                {
                    SType = StructureType.XlibSurfaceCreateInfoKhr,
                    Dpy = (nint*)Display,
                    Window = unchecked((nint)Window),
                };
                Result result = xlib.CreateXlibSurface(instance, &info, null, out SurfaceKHR surface);
                Assert.Equal(Result.Success, result);
                return surface;
            }
        }

        public void Dispose()
        {
            XDestroyWindow(Display, Window);
            XCloseDisplay(Display);
        }

        [DllImport("libX11.so.6", ExactSpelling = true)]
        private static extern int XInitThreads();

        [DllImport("libX11.so.6", ExactSpelling = true)]
        private static extern nint XOpenDisplay(nint displayName);

        [DllImport("libX11.so.6", ExactSpelling = true)]
        private static extern nuint XDefaultRootWindow(nint display);

        [DllImport("libX11.so.6", ExactSpelling = true)]
        private static extern nuint XCreateSimpleWindow(nint display, nuint parent, int x, int y, uint width, uint height, uint borderWidth, nuint border, nuint background);

        [DllImport("libX11.so.6", ExactSpelling = true)]
        private static extern int XMapWindow(nint display, nuint window);

        [DllImport("libX11.so.6", ExactSpelling = true)]
        private static extern int XSync(nint display, int discard);

        [DllImport("libX11.so.6", ExactSpelling = true)]
        private static extern int XDestroyWindow(nint display, nuint window);

        [DllImport("libX11.so.6", ExactSpelling = true)]
        private static extern int XCloseDisplay(nint display);
    }
}
