using System.Diagnostics;
using System.Drawing;

using Lumyte.Graphics.Vulkan;
using Lumyte.Input;
using Lumyte.Platform;
using Lumyte.Platform.SilkNet;

using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan.Samples;

internal static unsafe class Program
{
    public static int Main(string[] args)
    {
        int frameLimit = ParseFrameLimit(args);
        SampleKind selectedSample = ParseSample(args);
        using var platform = new SilkPlatform();
        using SilkWindow window = platform.CreateVulkanWindow(new WindowOptions
        {
            Title = SamplePresentation.Title(selectedSample),
            ClientSize = new Size(960, 540),
            IsVisible = true,
        });
        uint extensionCount = 0;
        byte** extensions = window.Native.VkSurface!.GetRequiredExtensions(out extensionCount);
        using VulkanDevice device = VulkanDevice.Create(extensionCount, extensions);
        VkNonDispatchableHandle surface = window.Native.VkSurface.Create<AllocationCallbacks>(
            new(device.InstanceHandle),
            null);
        using var presentation = new VulkanPresentation(
            device,
            surface.Handle,
            checked((uint)window.FramebufferSize.Width),
            checked((uint)window.FramebufferSize.Height));
        using var renderer = new VulkanSampleRenderer(device, presentation.ColorFormat);
        using GpuSemaphore timeline = device.MainQueue.CreateSemaphore();
        var stopwatch = Stopwatch.StartNew();
        ulong frameValue = 0;
        bool resize = false;

        window.Resized += (_, _) => resize = true;
        foreach (SilkKeyboard keyboard in window.WindowInput.Keyboards)
        {
            keyboard.KeyChanged += (_, change) =>
            {
                if (!change.IsPressed || change.IsRepeat)
                {
                    return;
                }

                if (change.Key == Key.Enter)
                {
                    selectedSample = SamplePresentation.Next(selectedSample);
                    window.Title = SamplePresentation.Title(selectedSample);
                }
                else if (change.Key == Key.Escape)
                {
                    window.Close();
                }
            };
        }

        while (!window.IsCloseRequested
            && platform.PumpEvents()
            && (frameLimit == 0 || frameValue < (ulong)frameLimit))
        {
            Size framebuffer = window.FramebufferSize;
            if (framebuffer.Width == 0 || framebuffer.Height == 0)
            {
                Thread.Sleep(16);
                continue;
            }

            if (resize)
            {
                presentation.Resize(
                    checked((uint)framebuffer.Width),
                    checked((uint)framebuffer.Height));
                resize = false;
            }

            VulkanPresentationFrame? acquired = presentation.Acquire();
            if (acquired is null)
            {
                resize = true;
                continue;
            }

            VulkanPresentationFrame frame = acquired.Value;
            GpuCommandBuffer commands = renderer.Record(
                selectedSample,
                frame,
                presentation.Width,
                presentation.Height,
                (float)stopwatch.Elapsed.TotalSeconds);
            frameValue++;
            resize |= presentation.SubmitAndPresent(
                frame,
                commands,
                timeline,
                frameValue);
            device.MainQueue.Wait(timeline, frameValue);
        }

        return 0;
    }

    private static int ParseFrameLimit(string[] args)
    {
        int index = Array.IndexOf(args, "--frames");
        return index >= 0
            && index + 1 < args.Length
            && int.TryParse(args[index + 1], out int value)
            && value >= 0
                ? value
                : 0;
    }

    private static SampleKind ParseSample(string[] args)
    {
        int index = Array.IndexOf(args, "--sample");
        if (index < 0 || index + 1 >= args.Length)
        {
            return SampleKind.Clear;
        }

        return args[index + 1].ToLowerInvariant() switch
        {
            "clear" => SampleKind.Clear,
            "triangle" => SampleKind.RainbowTriangle,
            "texture" => SampleKind.GeneratedTexture,
            "lighting" => SampleKind.RenderGraphLighting,
            _ => throw new ArgumentException(
                "--sample must be clear, triangle, texture, or lighting.",
                nameof(args)),
        };
    }
}
