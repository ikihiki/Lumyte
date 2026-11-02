using System.Diagnostics;
using System.Text.Json;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Vulkan;

namespace Lumyte.Graphics.Benchmarks;

internal static class NativeStages
{
    internal static async Task RunAsync()
    {
        if (Environment.GetEnvironmentVariable("VK_DRIVER_FILES") != "/usr/share/vulkan/icd.d/radeon_icd.json")
        {
            throw new InvalidOperationException("Select the verified Radeon ICD explicitly; software fallback is forbidden.");
        }

        using var device = VulkanDevice.Create();
        foreach (int count in new[] { 100, 1000, 1024, 1025, 10000 })
        {
            using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
            using IGraphicsTexture texture = device.CreateTexture(new() { Width = 800, Height = 800, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment });
            using IGraphicsTextureView view = texture.CreateView();
            var workload = new RecordingBenchmarks { Count = count };
            for (int iteration = 0; iteration < 26; iteration++)
            {
                long allocated = GC.GetTotalAllocatedBytes(false);
                long start = Stopwatch.GetTimestamp();
                DrawList2D list = workload.Rectangles();
                double build = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                start = Stopwatch.GetTimestamp();
                using PreparedDraw2D prepared = await renderer.PrepareAsync(list, new() { View = view, LoadOp = AttachmentLoadOp.Clear });
                double prepare = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
                commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = iteration == 0 ? TextureState.Undefined : TextureState.ColorAttachment, AfterState = TextureState.ColorAttachment, Before = iteration == 0 ? default : new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite), After = new(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite) });
                start = Stopwatch.GetTimestamp();
                prepared.Record(commands);
                commands.Finish();
                double record = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                start = Stopwatch.GetTimestamp();
                using IGraphicsSubmission submission = device.Queue.Submit([commands]);
                double submit = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                await submission.WaitAsync();
                double submitCompletion = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                Console.WriteLine(JsonSerializer.Serialize(new { count, iteration, phase = iteration == 0 ? "cold-renderer" : iteration < 6 ? "warmup" : "warm", build, prepare, record, submit, submitCompletion, allocated = GC.GetTotalAllocatedBytes(false) - allocated, expectedRectangleDraws = (count + 1023) / 1024 }));
            }
        }
    }
}
