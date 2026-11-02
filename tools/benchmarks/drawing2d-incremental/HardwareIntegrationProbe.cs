using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Vulkan;

namespace Lumyte.Graphics.Incremental.Benchmarks;

internal static class HardwareIntegrationProbe
{
    private const int WarmupFrames = 41;
    private const int SampleFrames = 120;

    internal static async Task RunAsync()
    {
        if (Environment.GetEnvironmentVariable("VK_DRIVER_FILES") != "/usr/share/vulkan/icd.d/radeon_icd.json")
        {
            throw new InvalidOperationException("Select the Radeon ICD explicitly; software fallback is forbidden.");
        }

        using var device = VulkanDevice.Create();
        using (var identity = new HardwareGpuTimer(device))
        {
            Console.WriteLine(JsonSerializer.Serialize(new { check = "active-device", device = identity.DeviceDescription, gpuTimestamp = identity.Supported, baseline = Environment.GetEnvironmentVariable("DRAWING_BASELINE") }));
        }

        foreach (int count in new[] { 1000, 1024, 1025, 10000 })
        {
            foreach (string pattern in new[] { "static", "single", "all" })
            {
                bool reverse = Environment.GetEnvironmentVariable("HARDWARE_REVERSE") == "1";
                string first = await RunStagesAsync(device, count, pattern, reverse);
                string second = await RunStagesAsync(device, count, pattern, !reverse);
                string reference = reverse ? second : first;
                string compatibility = reverse ? first : second;
                Verify(reference, compatibility, "stages", count, pattern, 1);
            }
        }

        foreach (int count in new[] { 1000, 10000 })
        {
            foreach (string pattern in new[] { "static", "single", "all" })
            {
                foreach (int depth in new[] { 1, 3 })
                {
                    bool reverse = Environment.GetEnvironmentVariable("HARDWARE_REVERSE") == "1";
                    string first = await RunPipelineAsync(device, count, pattern, reverse, depth);
                    string second = await RunPipelineAsync(device, count, pattern, !reverse, depth);
                    string reference = reverse ? second : first;
                    string compatibility = reverse ? first : second;
                    Verify(reference, compatibility, "pipeline", count, pattern, depth);
                }
            }
        }
    }

    private static void Verify(string reference, string actual, string scope, int count, string pattern, int depth)
    {
        if (reference != actual)
        {
            throw new InvalidOperationException("Hardware pixel readback differs between flat and retained+flatten.");
        }

        Console.WriteLine(JsonSerializer.Serialize(new { check = "hardware-pixels", scope, count, pattern, depth, sha256 = reference, status = "passed" }));
    }

    private static DrawList2D Update(ComparisonCase state, bool retained) => retained ? state.RetainedAndFlattenFrame() : state.LegacyFrame();

    private static async Task<string> RunStagesAsync(VulkanDevice device, int count, string pattern, bool retained)
    {
        var state = new ComparisonCase(count, pattern, "rectangles", 1, retained, retained);
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        using var slot = new FrameSlot(device);
        var samples = new List<object>(WarmupFrames + SampleFrames);
        for (int iteration = 0; iteration < WarmupFrames + SampleFrames; iteration++)
        {
            long allocationStart = GC.GetTotalAllocatedBytes(false);
            long frameStart = Stopwatch.GetTimestamp();
            DrawList2D list = Update(state, retained);
            double update = Stopwatch.GetElapsedTime(frameStart).TotalMilliseconds;
            long start = Stopwatch.GetTimestamp();
            slot.Prepared = await renderer.PrepareAsync(list, new() { View = slot.View, LoadOp = AttachmentLoadOp.Clear });
            double prepare = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            slot.CreateCommands(device);
            start = Stopwatch.GetTimestamp();
            if (slot.Timer.Supported)
            {
                slot.Timer.Begin(slot.Commands!);
            }

            slot.Prepared.Record(slot.Commands!);
            if (slot.Timer.Supported)
            {
                slot.Timer.End(slot.Commands!);
            }

            slot.Commands!.Finish();
            double record = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            start = Stopwatch.GetTimestamp();
            slot.Submission = device.Queue.Submit([slot.Commands]);
            double submit = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            await slot.Submission.WaitAsync();
            double submitCompletion = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            double? gpu = slot.Timer.Supported ? slot.Timer.ReadMilliseconds() : null;
            start = Stopwatch.GetTimestamp();
            slot.ReleaseCompleted();
            double dispose = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            double frameWall = Stopwatch.GetElapsedTime(frameStart).TotalMilliseconds;
            long allocated = GC.GetTotalAllocatedBytes(false) - allocationStart;
            samples.Add(new { scope = "stages", baseline = Environment.GetEnvironmentVariable("DRAWING_BASELINE"), mode = retained ? "retained+flatten" : "legacy", backend = "hardware-radv", count, pattern, iteration, phase = iteration == 0 ? "cold-renderer" : iteration < WarmupFrames ? "warmup" : "warm", update, prepare, record, submit, submitCompletion, dispose, frameWall, gpuRecordedSpanMs = gpu, allocatedProcessWide = allocated, framesInFlight = 1 });
        }

        foreach (object sample in samples)
        {
            Console.WriteLine(JsonSerializer.Serialize(sample));
        }

        return await ReadPixelsAsync(device, slot);
    }

    private static async Task<string> RunPipelineAsync(VulkanDevice device, int count, string pattern, bool retained, int depth)
    {
        var state = new ComparisonCase(count, pattern, "rectangles", 1, retained, retained);
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        FrameSlot[] slots = Enumerable.Range(0, depth).Select(_ => new FrameSlot(device)).ToArray();
        var gpuTimes = new List<double>(SampleFrames);
        try
        {
            for (int i = 0; i < WarmupFrames; i++)
            {
                FrameSlot slot = slots[i % depth];
                await RetireAsync(slot, null);
                await SubmitAsync(device, renderer, slot, Update(state, retained));
            }

            foreach (FrameSlot slot in slots)
            {
                await RetireAsync(slot, null);
            }

            long allocatedStart = GC.GetTotalAllocatedBytes(false);
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < SampleFrames; i++)
            {
                FrameSlot slot = slots[i % depth];
                await RetireAsync(slot, gpuTimes);
                await SubmitAsync(device, renderer, slot, Update(state, retained));
            }

            foreach (FrameSlot slot in slots)
            {
                await RetireAsync(slot, gpuTimes);
            }

            double elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            long allocated = GC.GetTotalAllocatedBytes(false) - allocatedStart;
            Console.WriteLine(JsonSerializer.Serialize(new { scope = "pipeline", baseline = Environment.GetEnvironmentVariable("DRAWING_BASELINE"), mode = retained ? "retained+flatten" : "legacy", backend = "hardware-radv", count, pattern, framesInFlight = depth, frames = SampleFrames, elapsedMs, headlessFramesPerSecond = SampleFrames * 1000 / elapsedMs, allocatedProcessWide = allocated, gpuRecordedSpanMs = gpuTimes, includesFinalDrain = true, includesRecordingAndDisposal = true, presentation = false }));
            return await ReadPixelsAsync(device, slots[(SampleFrames - 1) % depth]);
        }
        finally
        {
            foreach (FrameSlot slot in slots)
            {
                await RetireAsync(slot, null);
                slot.Dispose();
            }
        }
    }

    private static async Task SubmitAsync(VulkanDevice device, Renderer2D renderer, FrameSlot slot, DrawList2D list)
    {
        slot.Prepared = await renderer.PrepareAsync(list, new() { View = slot.View, LoadOp = AttachmentLoadOp.Clear });
        slot.CreateCommands(device);
        if (slot.Timer.Supported)
        {
            slot.Timer.Begin(slot.Commands!);
        }

        slot.Prepared.Record(slot.Commands!);
        if (slot.Timer.Supported)
        {
            slot.Timer.End(slot.Commands!);
        }

        slot.Commands!.Finish();
        slot.Submission = device.Queue.Submit([slot.Commands]);
    }

    private static async Task RetireAsync(FrameSlot slot, List<double>? times)
    {
        if (slot.Submission is null)
        {
            return;
        }

        await slot.Submission.WaitAsync();
        if (slot.Timer.Supported)
        {
            double elapsed = slot.Timer.ReadMilliseconds();
            times?.Add(elapsed);
        }

        slot.ReleaseCompleted();
    }

    private static async Task<string> ReadPixelsAsync(VulkanDevice device, FrameSlot slot)
    {
        const uint Pitch = 4096;
        using IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new() { Count = Pitch * 1024UL, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        using IGraphicsCommandBuffer copy = device.CreateCommandBuffer(new());
        copy.Barrier(new TextureBarrierDesc { Texture = slot.Texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.CopySource, Before = new(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        copy.CopyTextureToBuffer(new() { Texture = slot.Texture, Width = 1024, Height = 1024 }, new() { Buffer = readback.SliceElements(0, readback.Count), BytesPerRow = Pitch, RowsPerImage = 1024 });
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

    private sealed class FrameSlot : IDisposable
    {
        private bool _initialized;

        internal FrameSlot(VulkanDevice device)
        {
            Texture = device.CreateTexture(new() { Width = 1024, Height = 1024, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
            View = Texture.CreateView();
            Timer = new HardwareGpuTimer(device);
        }

        internal IGraphicsTexture Texture { get; }

        internal IGraphicsTextureView View { get; }

        internal HardwareGpuTimer Timer { get; }

        internal PreparedDraw2D? Prepared { get; set; }

        internal IGraphicsCommandBuffer? Commands { get; set; }

        internal IGraphicsSubmission? Submission { get; set; }

        public void Dispose()
        {
            if (Submission is not null)
            {
                throw new InvalidOperationException("GPU completion must be confirmed before slot disposal.");
            }

            Commands?.Dispose();
            Prepared?.Dispose();
            Timer.Dispose();
            View.Dispose();
            Texture.Dispose();
        }

        internal void CreateCommands(VulkanDevice device)
        {
            Commands = device.CreateCommandBuffer(new());
            var color = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite);
            Commands.Barrier(new TextureBarrierDesc { Texture = Texture, Range = new(0, 1, 0, 1), BeforeState = _initialized ? TextureState.ColorAttachment : TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = _initialized ? color : default, After = color });
            _initialized = true;
        }

        internal void ReleaseCompleted()
        {
            Submission!.Dispose();
            Commands!.Dispose();
            Prepared!.Dispose();
            Submission = null;
            Commands = null;
            Prepared = null;
        }
    }
}
