using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Vulkan;

namespace Lumyte.Graphics.Incremental.Benchmarks;

internal static class DirectHardwareProbe
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
            Emit(new { check = "active-device", device = identity.DeviceDescription, gpuTimestamp = identity.Supported, baseline = Environment.GetEnvironmentVariable("DRAWING_BASELINE"), prototype = "direct-resident-rectangles" });
        }

        await CheckResidencyAsync(device);
        foreach (int count in new[] { 1000, 1024, 1025, 10000 })
        {
            foreach (string pattern in new[] { "static", "single", "clustered", "scattered", "all" })
            {
                foreach (int depth in new[] { 0, 1, 3 })
                {
                    string? reference = null;
                    string[] modes = Environment.GetEnvironmentVariable("HARDWARE_REVERSE") == "1" ? ["direct", "retained+flatten", "legacy"] : ["legacy", "retained+flatten", "direct"];
                    foreach (string mode in modes)
                    {
                        string hash = await RunCaseAsync(device, count, pattern, mode, depth);
                        DirectRectangleChecks.Require(reference is null || reference == hash, "Three-mode hardware pixels differ.");
                        reference = hash;
                    }

                    Emit(new { check = "direct-hardware-pixels", status = "passed", count, pattern, scope = depth == 0 ? "stages" : "pipeline", framesInFlight = Math.Max(1, depth), sha256 = reference });
                }
            }
        }
    }

    private static async Task<string> RunCaseAsync(VulkanDevice device, int count, string pattern, string mode, int depth)
    {
        var input = new Inputs(count, pattern, mode);
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        using var directRenderer = new DirectRectangleRenderer(device);
        FrameSlot[] slots = Enumerable.Range(0, Math.Max(1, depth)).Select(_ => new FrameSlot(device, mode == "direct" ? directRenderer.CreateSlot(input.Direct!.Scene) : null)).ToArray();
        var samples = new List<object>();
        var gpuTimes = new List<double>();
        long uploadBytes = 0;
        long dirtyChunks = 0;
        long packedCommands = 0;
        long gpuAllocations = 0;
        long draws = 0;
        try
        {
            for (int i = 0; i < WarmupFrames; i++)
            {
                FrameSlot slot = slots[i % slots.Length];
                await RetireAsync(slot);
                input.Update();
                await PrepareAsync(renderer, slot, input);
                Record(device, slot);
                Submit(device, slot);
            }

            foreach (FrameSlot slot in slots)
            {
                await RetireAsync(slot); // Read and discard completed warmup timestamps before reuse.
            }

            long allocatedStart = GC.GetTotalAllocatedBytes(false);
            long windowStart = Stopwatch.GetTimestamp();
            for (int i = 0; i < SampleFrames; i++)
            {
                FrameSlot slot = slots[i % slots.Length];
                double? previousGpu = await RetireAsync(slot);
                if (previousGpu.HasValue)
                {
                    gpuTimes.Add(previousGpu.Value);
                }

                long allocationStart = GC.GetTotalAllocatedBytes(false);
                long frameStart = Stopwatch.GetTimestamp();
                input.Update();
                double update = Milliseconds(frameStart);
                long start = Stopwatch.GetTimestamp();
                await PrepareAsync(renderer, slot, input);
                double prepare = Milliseconds(start);
                long? bytes = slot.DirectFrame?.UploadedBytes;
                int? chunks = slot.DirectFrame?.DirtyChunks;
                int? drawCount = slot.DirectFrame?.DrawCount;
                int? allocations = slot.DirectFrame?.GpuAllocations;
                if (mode == "direct")
                {
                    RequireCounters(input, slot, count, pattern);
                    uploadBytes += bytes!.Value;
                    dirtyChunks += chunks!.Value;
                    packedCommands += input.Direct!.PackedCommands;
                    draws += drawCount!.Value;
                    gpuAllocations += allocations!.Value;
                }

                start = Stopwatch.GetTimestamp();
                Record(device, slot);
                double record = Milliseconds(start);
                start = Stopwatch.GetTimestamp();
                Submit(device, slot);
                double submit = Milliseconds(start);
                if (depth == 0)
                {
                    await slot.Submission!.WaitAsync();
                    double submitCompletion = Milliseconds(start);
                    double? gpu = ReadTimer(slot);
                    start = Stopwatch.GetTimestamp();
                    slot.ReleaseCompleted();
                    double dispose = Milliseconds(start);
                    double frameWall = Milliseconds(frameStart);
                    long allocated = GC.GetTotalAllocatedBytes(false) - allocationStart;
                    samples.Add(new { scope = "stages", baseline = Environment.GetEnvironmentVariable("DRAWING_BASELINE"), mode, count, pattern, iteration = i, phase = "warm", update, prepare, record, submit, submitCompletion, dispose, frameWall, gpuRecordedSpanMs = gpu, allocatedProcessWide = allocated, framesInFlight = 1, directPackedCommands = input.Direct?.PackedCommands, copiedCpuBytes = input.Direct?.CopiedCpuBytes, uploadedBytes = bytes, dirtyChunks = chunks, drawCount, gpuAllocations = allocations });
                }
            }

            foreach (FrameSlot slot in slots)
            {
                double? gpu = await RetireAsync(slot);
                if (gpu.HasValue)
                {
                    gpuTimes.Add(gpu.Value);
                }
            }

            double elapsedMs = Milliseconds(windowStart);
            long allocatedTotal = GC.GetTotalAllocatedBytes(false) - allocatedStart;
            foreach (object sample in samples)
            {
                Emit(sample);
            }

            if (depth != 0)
            {
                Emit(new { scope = "pipeline", baseline = Environment.GetEnvironmentVariable("DRAWING_BASELINE"), mode, count, pattern, framesInFlight = depth, frames = SampleFrames, elapsedMs, headlessFramesPerSecond = SampleFrames * 1000 / elapsedMs, allocatedProcessWide = allocatedTotal, gpuRecordedSpanMs = gpuTimes, includesFinalDrain = true, includesRecordingAndDisposal = true, presentation = false, directPackedCommands = mode == "direct" ? (long?)packedCommands : null, uploadedBytes = mode == "direct" ? (long?)uploadBytes : null, dirtyChunks = mode == "direct" ? (long?)dirtyChunks : null, drawCount = mode == "direct" ? (long?)draws : null, gpuAllocations = mode == "direct" ? (long?)gpuAllocations : null });
            }

            return await ReadPixelsAsync(device, slots[(SampleFrames - 1) % slots.Length]);
        }
        finally
        {
            foreach (FrameSlot slot in slots)
            {
                await RetireAsync(slot);
                slot.Dispose();
            }
        }
    }

    private static void RequireCounters(Inputs input, FrameSlot slot, int count, string pattern)
    {
        DirectRectangleRenderer.Slot.Frame frame = slot.DirectFrame!;
        DirectRectangleChecks.Require(frame.GpuAllocations == 0 && frame.PackedCommands == 0, "Warm renderer allocated or packed GPU command payloads.");
        DirectRectangleChecks.Require(frame.DrawCount == (count + 1023) / 1024, "Unexpected direct draw count.");
        if (pattern == "static")
        {
            DirectRectangleChecks.Require(frame.UploadedBytes == 0 && frame.DirtyChunks == 0 && input.Direct!.PackedCommands == 0, "Static resident scene repacked or uploaded.");
        }
        else if (pattern == "single")
        {
            DirectRectangleChecks.Require(frame.UploadedBytes > 0 && frame.UploadedBytes < count * 96L, "Sparse catch-up uploaded a full scene.");
        }
        else if (pattern == "all")
        {
            DirectRectangleChecks.Require(frame.UploadedBytes == count * 96L, "Full update upload is incomplete.");
        }
    }

    private static async Task PrepareAsync(Renderer2D renderer, FrameSlot slot, Inputs input)
    {
        if (slot.DirectSlot is not null)
        {
            slot.DirectFrame = await slot.DirectSlot.PrepareAsync(input.Direct!.Scene);
        }
        else
        {
            slot.Prepared = await renderer.PrepareAsync(input.List!, new() { View = slot.View, LoadOp = AttachmentLoadOp.Clear });
        }
    }

    private static void Record(VulkanDevice device, FrameSlot slot)
    {
        slot.CreateCommands(device);
        if (slot.Timer.Supported)
        {
            slot.Timer.Begin(slot.Commands!);
        }

        if (slot.DirectFrame is not null)
        {
            slot.DirectFrame.Record(slot.Commands!, slot.View);
        }
        else
        {
            slot.Prepared!.Record(slot.Commands!);
        }

        if (slot.Timer.Supported)
        {
            slot.Timer.End(slot.Commands!);
        }

        slot.Commands!.Finish();
    }

    private static void Submit(VulkanDevice device, FrameSlot slot)
    {
        slot.Submission = device.Queue.Submit([slot.Commands!]);
        slot.DirectFrame?.Submitted(slot.Submission);
    }

    private static double? ReadTimer(FrameSlot slot) => slot.Timer.Supported ? slot.Timer.ReadMilliseconds() : null;

    private static async Task<double?> RetireAsync(FrameSlot slot)
    {
        if (slot.Submission is null)
        {
            return null;
        }

        await slot.Submission.WaitAsync();
        double? gpu = ReadTimer(slot);
        slot.ReleaseCompleted();
        return gpu;
    }

    private static async Task CheckResidencyAsync(VulkanDevice device)
    {
        var workload = new DirectRectangleWorkload(1025, "single");
        using var renderer = new DirectRectangleRenderer(device);
        using var slot = new FrameSlot(device, renderer.CreateSlot(workload.Scene));
        DirectRectangleRenderer.Slot residentSlot = slot.DirectSlot!;

        // Never-submitted preparation cannot publish residency.
        using (DirectRectangleRenderer.Slot.Frame abandoned = await residentSlot.PrepareAsync(workload.Scene))
        {
            DirectRectangleChecks.Require(abandoned.UploadedBytes == 1025 * 96L, "First prepare must upload all data.");
            DirectRectangleChecks.Reject<InvalidOperationException>(abandoned.Complete);
        }

        // Recording without submission also must not publish residency.
        DirectRectangleRenderer.Slot.Frame recorded = await residentSlot.PrepareAsync(workload.Scene);
        using (IGraphicsCommandBuffer neverSubmitted = device.CreateCommandBuffer(new()))
        {
            recorded.Record(neverSubmitted, slot.View);
            DirectRectangleChecks.Reject<InvalidOperationException>(recorded.Complete);
        }

        recorded.AbortUnsubmitted();
        recorded.Dispose();
        slot.DirectFrame = await residentSlot.PrepareAsync(workload.Scene);
        DirectRectangleChecks.Require(slot.DirectFrame.UploadedBytes == 1025 * 96L, "Abandoned prepare incorrectly published residency.");
        DirectRectangleChecks.Reject<InvalidOperationException>(() => residentSlot.PrepareAsync(workload.Scene).AsTask().GetAwaiter().GetResult());
        Record(device, slot);
        DirectRectangleChecks.Reject<InvalidOperationException>(() => slot.DirectFrame.Record(slot.Commands!, slot.View));
        DirectRectangleChecks.Reject<InvalidOperationException>(slot.DirectFrame.Complete);
        DirectRectangleChecks.Reject<InvalidOperationException>(residentSlot.Dispose);
        Submit(device, slot);
        await RetireAsync(slot);
        using (DirectRectangleRenderer.Slot.Frame resident = await residentSlot.PrepareAsync(workload.Scene))
        {
            DirectRectangleChecks.Require(resident.UploadedBytes == 0 && resident.DirtyChunks == 0, "Completed residency was not retained.");
        }

        // Catch up across CPU updates that this slot did not see, spanning page and draw-run edges.
        DirectRectangleScene old = workload.Scene;
        DirectRectangleScene current = old;
        foreach (int index in new[] { 63, 64, 1023, 1024 })
        {
            current = current.Update([(index, current.GetCommand(index) with { Rect = new(200, 200, 7, 7), Paint = PaintGraph2D.Solid(index % 2 == 0 ? new(0.2f, 0.4f, 0.8f, 1) : new(0.9f, 0.2f, 0.1f, 0.7f)) })]);
        }

        slot.DirectFrame = await residentSlot.PrepareAsync(current);
        DirectRectangleChecks.Require(slot.DirectFrame.DirtyChunks == 4 && slot.DirectFrame.UploadedBytes == ((64 * 3) + 1) * 96L, "Skipped-generation catch-up lost dirty pages.");
        Record(device, slot);
        Submit(device, slot);
        await RetireAsync(slot);
        string directHash = await ReadPixelsAsync(device, slot);
        var canvas = new Canvas2D();
        canvas.Begin();
        for (int i = 0; i < current.Count; i++)
        {
            FillRectCommand2D command = current.GetCommand(i);
            canvas.FillRect(command.Rect, command.Paint);
        }

        DrawList2D list = canvas.End(); // Untimed reference only, never on the direct measured path.
        using var referenceRenderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        using var reference = new FrameSlot(device, null);
        reference.Prepared = await referenceRenderer.PrepareAsync(list, new() { View = reference.View, LoadOp = AttachmentLoadOp.Clear });
        Record(device, reference);
        Submit(device, reference);
        await RetireAsync(reference);
        DirectRectangleChecks.Require(directHash == await ReadPixelsAsync(device, reference), "Catch-up pixels differ from a complete reference.");
        Emit(new { check = "direct-residency-lifetime-catchup", status = "passed", dirtyChunks = 4, uploadedBytes = ((64 * 3) + 1) * 96, sha256 = directHash, alphaOverlap = true, failedQueueSubmitInjected = false });
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

    private static double Milliseconds(long start) => Stopwatch.GetElapsedTime(start).TotalMilliseconds;

    private static void Emit(object value) => Console.WriteLine(JsonSerializer.Serialize(value));

    private sealed class Inputs
    {
        private readonly ComparisonCase? _comparison;
        private readonly string _mode;

        internal Inputs(int count, string pattern, string mode)
        {
            _mode = mode;
            if (mode == "direct")
            {
                Direct = new(count, pattern);
            }
            else
            {
                _comparison = new(count, pattern, "rectangles", 1, mode != "legacy", mode != "legacy");
            }
        }

        internal DirectRectangleWorkload? Direct { get; }

        internal DrawList2D? List { get; private set; }

        internal void Update()
        {
            if (Direct is not null)
            {
                Direct.Update();
            }
            else
            {
                List = _mode == "legacy" ? _comparison!.LegacyFrame() : _comparison!.RetainedAndFlattenFrame();
            }
        }
    }

    private sealed class FrameSlot : IDisposable
    {
        private bool _initialized;

        internal FrameSlot(VulkanDevice device, DirectRectangleRenderer.Slot? directSlot)
        {
            DirectSlot = directSlot;
            Texture = device.CreateTexture(new() { Width = 1024, Height = 1024, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
            View = Texture.CreateView();
            Timer = new HardwareGpuTimer(device);
        }

        internal IGraphicsTexture Texture { get; }

        internal IGraphicsTextureView View { get; }

        internal HardwareGpuTimer Timer { get; }

        internal DirectRectangleRenderer.Slot? DirectSlot { get; }

        internal DirectRectangleRenderer.Slot.Frame? DirectFrame { get; set; }

        internal PreparedDraw2D? Prepared { get; set; }

        internal IGraphicsCommandBuffer? Commands { get; set; }

        internal IGraphicsSubmission? Submission { get; set; }

        public void Dispose()
        {
            DirectRectangleChecks.Require(Submission is null, "Drain before slot disposal.");
            Commands?.Dispose();
            Prepared?.Dispose();
            DirectFrame?.Dispose();
            DirectSlot?.Dispose();
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
            DirectFrame?.Complete(); // Publish residency only after the associated actual submission completed.
            DirectFrame?.Dispose();
            Prepared?.Dispose();
            Submission!.Dispose();
            Commands!.Dispose();
            DirectFrame = null;
            Prepared = null;
            Submission = null;
            Commands = null;
        }
    }
}
