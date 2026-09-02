using System.Drawing;
using System.Security.Cryptography;
using System.Text;

using Lumyte.Graphics.Vulkan;
using Lumyte.Platform;
using Lumyte.Platform.SilkNet;
using Lumyte.Shaders;

using Silk.NET.Core.Native;
using Silk.NET.Shaderc;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan.Triangle;

internal static unsafe class Program
{
    private const string VertexShader = """
        #version 450
        const vec2 positions[3] = vec2[3](vec2(-0.8,-0.8), vec2(0.8,-0.8), vec2(0.0,0.8));
        void main() { gl_Position = vec4(positions[gl_VertexIndex], 0.0, 1.0); }
        """;
    private const string PixelShader = """
        #version 450
        layout(location=0) out vec4 color;
        void main() { color = vec4(1.0, 0.2, 0.1, 1.0); }
        """;

    public static int Main(string[] args)
    {
        int frameLimit = ParseFrameLimit(args);
        using var platform = new SilkPlatform();
        using SilkWindow window = platform.CreateVulkanWindow(new WindowOptions
        {
            Title = "Lumyte Vulkan Triangle",
            ClientSize = new Size(960, 540),
            IsVisible = true,
        });
        uint extensionCount = 0;
        byte** extensions = window.Native.VkSurface!.GetRequiredExtensions(out extensionCount);
        using VulkanDevice device = VulkanDevice.Create(extensionCount, extensions);
        VkNonDispatchableHandle surface = window.Native.VkSurface.Create<AllocationCallbacks>(new(device.InstanceHandle), null);
        using var presentation = new VulkanPresentation(device, surface.Handle,
            checked((uint)window.FramebufferSize.Width), checked((uint)window.FramebufferSize.Height));
        byte[] abiHash = SHA256.HashData(Encoding.UTF8.GetBytes("triangle-v1"));
        byte[] packageBytes = GpuShaderPackageWriter.Write([
            new(GpuShaderCodeFormat.SpirV, GpuShaderStage.Vertex, "triangleVertex", "vulkan", "spirv1.3", "", abiHash,
                TriangleShaderCompiler.Compile(VertexShader, ShaderKind.VertexShader)),
            new(GpuShaderCodeFormat.SpirV, GpuShaderStage.Pixel, "trianglePixel", "vulkan", "spirv1.3", "", abiHash,
                TriangleShaderCompiler.Compile(PixelShader, ShaderKind.FragmentShader))]);
        GpuShaderPackage package = GpuShaderPackage.Read(packageBytes);
        GpuRasterPipelineHandle pipeline = device.CreateRasterPipeline(
            new GpuRasterPipelineDescription([new(presentation.ColorFormat)]), package,
            "triangleVertex", "trianglePixel", abiHash);
        using GpuSemaphore timeline = device.MainQueue.CreateSemaphore();
        ulong frameValue = 0;
        bool resize = false;
        window.Resized += (_, _) => resize = true;

        while (!window.IsCloseRequested && platform.PumpEvents() && (frameLimit == 0 || frameValue < (ulong)frameLimit))
        {
            Size framebuffer = window.FramebufferSize;
            if (framebuffer.Width == 0 || framebuffer.Height == 0) { Thread.Sleep(16); continue; }
            if (resize)
            {
                presentation.Resize(checked((uint)framebuffer.Width), checked((uint)framebuffer.Height));
                resize = false;
            }
            VulkanPresentationFrame? acquired = presentation.Acquire();
            if (acquired is null) { resize = true; continue; }
            VulkanPresentationFrame frame = acquired.Value;
            var attachment = new GpuColorAttachment(frame.View, GpuAttachmentLoadOperation.Clear,
                GpuAttachmentStoreOperation.Store, new(0.02f, 0.03f, 0.06f, 1));
            GpuCommandBuffer commands = device.MainQueue.StartCommandRecording()
                .Barrier(GpuStage.None, GpuStage.ColorOutput)
                .BeginRendering([attachment])
                .SetPipeline(pipeline)
                .SetViewportAndScissor(new(0, 0, presentation.Width, presentation.Height),
                    new(0, 0, presentation.Width, presentation.Height))
                .Draw(3)
                .EndRendering();
            frameValue++;
            resize |= presentation.SubmitAndPresent(frame, commands, timeline, frameValue);
            device.MainQueue.Wait(timeline, frameValue);
        }

        device.DestroyRasterPipeline(pipeline);
        return 0;
    }

    private static int ParseFrameLimit(string[] args)
    {
        int index = Array.IndexOf(args, "--frames");
        return index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out int value) ? value : 0;
    }
}
