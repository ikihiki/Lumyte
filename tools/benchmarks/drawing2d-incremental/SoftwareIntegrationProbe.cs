using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Vulkan;

namespace Lumyte.Graphics.Incremental.Benchmarks;

internal static class SoftwareIntegrationProbe
{
    internal static async Task RunAsync()
    {
        string? manifest = Environment.GetEnvironmentVariable("LUMYTE_LAVAPIPE_ICD");
        if (manifest is null || Environment.GetEnvironmentVariable("VK_DRIVER_FILES") != manifest)
        {
            throw new InvalidOperationException("This diagnostic requires explicit project lavapipe; it is NOT a hardware GPU benchmark.");
        }

        using var device = VulkanDevice.Create();
        foreach (int count in new[] { 1000, 10000 })
        {
            foreach (string pattern in new[] { "static", "single", "all" })
            {
                string reference = await RunCaseAsync(device, count, pattern, false);
                string compatibility = await RunCaseAsync(device, count, pattern, true);
                if (reference != compatibility)
                {
                    throw new InvalidOperationException("Software GPU pixel readback differs between flat and retained+flatten.");
                }

                Console.WriteLine(JsonSerializer.Serialize(new { check = "software-gpu-pixels", count, pattern, sha256 = reference, status = "passed" }));
            }
        }
    }

    private static async Task<string> RunCaseAsync(IGraphicDevice device, int count, string pattern, bool retained)
    {
        var state = new ComparisonCase(count, pattern, "rectangles", 1, retained, retained);
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        using IGraphicsTexture texture = device.CreateTexture(new() { Width = 1024, Height = 1024, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
        using IGraphicsTextureView view = texture.CreateView();
        var colorScope = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite);
        for (int iteration = 0; iteration < 24; iteration++)
        {
            long allocationStart = GC.GetTotalAllocatedBytes(false);
            long start = Stopwatch.GetTimestamp();
            DrawList2D list = retained ? state.RetainedAndFlattenFrame() : state.LegacyFrame();
            double update = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            start = Stopwatch.GetTimestamp();
            PreparedDraw2D prepared = await renderer.PrepareAsync(list, new() { View = view, LoadOp = AttachmentLoadOp.Clear });
            double prepare = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
            commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = iteration == 0 ? TextureState.Undefined : TextureState.ColorAttachment, AfterState = TextureState.ColorAttachment, Before = iteration == 0 ? default : colorScope, After = colorScope });
            start = Stopwatch.GetTimestamp();
            prepared.Record(commands);
            commands.Finish();
            double record = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            start = Stopwatch.GetTimestamp();
            IGraphicsSubmission submission = device.Queue.Submit([commands]);
            double submit = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            await submission.WaitAsync();
            double submitCompletion = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            start = Stopwatch.GetTimestamp();
            submission.Dispose();
            commands.Dispose();
            prepared.Dispose();
            double dispose = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            Console.WriteLine(JsonSerializer.Serialize(new { baseline = Environment.GetEnvironmentVariable("DRAWING_BASELINE"), mode = retained ? "retained+flatten" : "legacy", backend = "software-vulkan", count, pattern, iteration, phase = iteration == 0 ? "cold-renderer" : iteration < 6 ? "warmup" : "warm", update, prepare, record, submit, submitCompletion, dispose, allocatedProcessWide = GC.GetTotalAllocatedBytes(false) - allocationStart, framesInFlight = 1, gpuTimestamp = false }));
        }

        const uint Pitch = 4096;
        using IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new() { Count = Pitch * 1024UL, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        using IGraphicsCommandBuffer copy = device.CreateCommandBuffer(new());
        copy.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.CopySource, Before = colorScope, After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        copy.CopyTextureToBuffer(new() { Texture = texture, Width = 1024, Height = 1024 }, new() { Buffer = readback.SliceElements(0, readback.Count), BytesPerRow = Pitch, RowsPerImage = 1024 });
        copy.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.SliceElements(0, readback.Count), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        copy.Finish();
        using IGraphicsSubmission copied = device.Queue.Submit([copy]);
        await copied.WaitAsync();
        await readback.MapAsync();
        byte[] pixels = new byte[checked((int)readback.Count)];
        readback.CopyTo(pixels);
        readback.Unmap();
        return Convert.ToHexString(SHA256.HashData(pixels));
    }
}
