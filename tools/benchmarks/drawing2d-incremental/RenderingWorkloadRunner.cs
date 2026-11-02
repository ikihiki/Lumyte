using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains;
using BenchmarkDotNet.Toolchains.DotNetCli;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Vulkan;
using Perfolizer.Horology;

namespace Lumyte.Graphics.Incremental.Benchmarks;

internal static class RenderingWorkloadRunner
{
    internal static async Task RunAsync(string[] args)
    {
        var options = Options.Parse(args);
        string[] selected = RenderingWorkloadCatalog.Select(options.Suite, options.Case);
        if (options.Mode == "--workload-list")
        {
            foreach ((string Suite, string Name) item in RenderingWorkloadCatalog.Cases)
            {
                Emit(new { suite = item.Suite, name = item.Name });
            }

            return;
        }

        if (options.Mode == "--workload-cpu")
        {
            Environment.SetEnvironmentVariable("LUMYTE_WORKLOAD_SUITE", options.Suite);
            Environment.SetEnvironmentVariable("LUMYTE_WORKLOAD_CASE", options.Case);
            Environment.SetEnvironmentVariable("LUMYTE_WORKLOAD_COUNT", options.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            IConfig config = ManualConfig.Create(DefaultConfig.Instance)
                .AddJob(Job.Default.WithId("rendering-workloads")
                    .WithToolchain(new Toolchain("ExplicitProject", new ExplicitProjectGenerator(), new DotNetCliBuilder("net10.0"), new DotNetCliExecutor(null!)))
                    .WithLaunchCount(1).WithWarmupCount(options.Quick ? 1 : 3).WithIterationCount(options.Quick ? 3 : 8)
                    .WithIterationTime(TimeInterval.FromMilliseconds(options.Quick ? 50 : 150))
                    .WithOutlierMode(Perfolizer.Mathematics.OutlierDetection.OutlierMode.DontRemove))
                .AddDiagnoser(MemoryDiagnoser.Default).AddColumn(StatisticColumn.Median, StatisticColumn.P95).AddExporter(JsonExporter.Full);
            BenchmarkSwitcher.FromTypes([typeof(RenderingWorkloadCpuBenchmarks)]).Run(["--filter", "*", "--artifacts", options.Artifacts], config);
            return;
        }

        if (options.Mode == "--workload-smoke")
        {
            foreach (string name in selected)
            {
                using IRenderingWorkload workload = RenderingWorkloadCatalog.Create(name, options.Count);
                for (int i = 0; i < 5; i++)
                {
                    workload.Update();
                    workload.Layout();
                    IReadOnlyList<DrawList2D> lists = workload.Record();
                    if (lists.Count == 0 || lists.Sum(l => l.Commands.Count) == 0)
                    {
                        throw new InvalidOperationException("Empty workload recording.");
                    }

                    workload.Verify();
                }

                Emit(new { check = "workload-cpu-smoke", suite = workload.Suite, workload = workload.Name, status = "passed", frames = 5, count = options.Count, description = workload.Description, counters = workload.Counters });
            }

            return;
        }

        if (Environment.GetEnvironmentVariable("VK_DRIVER_FILES") != "/usr/share/vulkan/icd.d/radeon_icd.json")
        {
            throw new InvalidOperationException("Hardware workloads require explicit Radeon ICD; no software fallback.");
        }

        using var device = VulkanDevice.Create();
        using (var identity = new HardwareGpuTimer(device))
        {
            Emit(new { check = "active-device", device = identity.DeviceDescription, gpuTimestamp = identity.Supported, source = Environment.GetEnvironmentVariable("LUMYTE_WORKLOAD_REVISION"), suite = options.Suite, count = options.Count, warmup = options.Warmup, samples = options.Samples, depth = options.Depth, scope = options.Pipeline ? "pipeline" : "stages", tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"), gcServer = Environment.GetEnvironmentVariable("DOTNET_gcServer") });
        }

        foreach (string name in selected)
        {
            await RunHardwareAsync(device, name, options);
        }
    }

    private static async Task RunHardwareAsync(VulkanDevice device, string name, Options options)
    {
        long setupStart = Stopwatch.GetTimestamp();
        using IRenderingWorkload workload = RenderingWorkloadCatalog.Create(name, options.Count);
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        WorkloadSlot[] slots = Enumerable.Range(0, options.Depth).Select(_ => new WorkloadSlot(device)).ToArray();
        double setupMs = Ms(setupStart);
        var rows = new List<object>();
        var gpuTimes = new List<double>(options.Samples);
        try
        {
            for (int i = 0; i < options.Warmup; i++)
            {
                WorkloadSlot slot = slots[i % slots.Length];
                await RetireAsync(slot);
                workload.Update();
                workload.Layout();
                await PrepareAsync(renderer, slot, workload.Record());
                Encode(device, slot);
                slot.Submission = device.Queue.Submit([slot.Commands!]);
            }

            foreach (WorkloadSlot slot in slots)
            {
                await RetireAsync(slot);
            }

            long allStart = Stopwatch.GetTimestamp();
            long allAllocated = GC.GetTotalAllocatedBytes(false);
            for (int i = 0; i < options.Samples; i++)
            {
                WorkloadSlot slot = slots[i % slots.Length];
                double? retiredGpu = await RetireAsync(slot);
                if (retiredGpu.HasValue)
                {
                    gpuTimes.Add(retiredGpu.Value);
                }

                long textStart = Counter(workload, "text_layout_ticks");
                long allocatedStart = GC.GetTotalAllocatedBytes(false);
                long frameStart = Stopwatch.GetTimestamp();
                workload.Update();
                double updateMs = Ms(frameStart);
                long start = Stopwatch.GetTimestamp();
                workload.Layout();
                double layoutMs = Ms(start);
                double textLayoutMs = (Counter(workload, "text_layout_ticks") - textStart) * 1000.0 / Stopwatch.Frequency;
                start = Stopwatch.GetTimestamp();
                IReadOnlyList<DrawList2D> lists = workload.Record();
                double recordMs = Ms(start);
                int commandCount = lists.Sum(l => l.Commands.Count);
                start = Stopwatch.GetTimestamp();
                await PrepareAsync(renderer, slot, lists);
                double prepareMs = Ms(start);
                start = Stopwatch.GetTimestamp();
                Encode(device, slot);
                double encodeMs = Ms(start);
                start = Stopwatch.GetTimestamp();
                slot.Submission = device.Queue.Submit([slot.Commands!]);
                double submitMs = Ms(start);
                if (!options.Pipeline)
                {
                    await slot.Submission.WaitAsync();
                    double submitCompletionMs = Ms(start);
                    double? gpuMs = ReadTimer(slot);
                    start = Stopwatch.GetTimestamp();
                    slot.ReleaseCompleted();
                    double disposeMs = Ms(start);
                    rows.Add(new { scope = "stages", suite = workload.Suite, workload = name, description = workload.Description, count = options.Count, iteration = i, updateMs, layoutInclusiveMs = layoutMs, textLayoutNestedMs = textLayoutMs, recordMs, prepareMs, encodeMs, submitMs, submitCompletionCpuMs = submitCompletionMs, disposeMs, frameCpuWallMs = Ms(frameStart), gpuRecordedSpanMs = gpuMs, allocatedProcessWide = GC.GetTotalAllocatedBytes(false) - allocatedStart, layerCount = lists.Count, commandCount, counters = workload.Counters.ToDictionary(), presentation = false });
                }
            }

            foreach (WorkloadSlot slot in slots)
            {
                double? gpu = await RetireAsync(slot);
                if (gpu.HasValue)
                {
                    gpuTimes.Add(gpu.Value);
                }
            }

            double elapsedMs = Ms(allStart);
            long allocated = GC.GetTotalAllocatedBytes(false) - allAllocated;
            workload.Verify();
            foreach (object row in rows)
            {
                Emit(row);
            }

            if (options.Pipeline)
            {
                Emit(new { scope = "pipeline", suite = workload.Suite, workload = name, count = options.Count, framesInFlight = options.Depth, frames = options.Samples, elapsedMs, headlessFramesPerSecond = options.Samples * 1000.0 / elapsedMs, gpuRecordedSpanMs = gpuTimes, allocatedProcessWide = allocated, includesFinalDrain = true, presentation = false, counters = workload.Counters });
            }

            string hash = await ReadPixelsAsync(device, slots[(options.Samples - 1) % slots.Length]);
            Emit(new { check = "workload-hardware-readback", suite = workload.Suite, workload = name, status = "passed", sha256 = hash, setupMs, warmup = options.Warmup, samples = options.Samples, scope = options.Pipeline ? "pipeline" : "stages", counters = workload.Counters });
        }
        finally
        {
            foreach (WorkloadSlot slot in slots)
            {
                await RetireAsync(slot);
                slot.Dispose();
            }
        }
    }

    private static async Task PrepareAsync(Renderer2D renderer, WorkloadSlot slot, IReadOnlyList<DrawList2D> layers)
    {
        if (layers.Count == 0)
        {
            throw new InvalidOperationException("A workload must produce at least one layer.");
        }

        slot.Roots = layers.ToArray();
        foreach (DrawList2D layer in layers)
        {
            slot.Prepared.Add(await renderer.PrepareAsync(layer, new() { View = slot.View, LoadOp = slot.Prepared.Count == 0 ? AttachmentLoadOp.Clear : AttachmentLoadOp.Load }));
        }
    }

    private static void Encode(VulkanDevice device, WorkloadSlot slot)
    {
        slot.Commands = device.CreateCommandBuffer(new());
        var color = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite);
        slot.Commands.Barrier(new TextureBarrierDesc { Texture = slot.Texture, Range = new(0, 1, 0, 1), BeforeState = slot.Initialized ? TextureState.ColorAttachment : TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = slot.Initialized ? color : default, After = color });
        slot.Initialized = true;
        if (slot.Timer.Supported)
        {
            slot.Timer.Begin(slot.Commands);
        }

        for (int i = 0; i < slot.Prepared.Count; i++)
        {
            if (i != 0)
            {
                slot.Commands.Barrier(new TextureBarrierDesc { Texture = slot.Texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.ColorAttachment, Before = color, After = color });
            }

            slot.Prepared[i].Record(slot.Commands);
        }

        if (slot.Timer.Supported)
        {
            slot.Timer.End(slot.Commands);
        }

        slot.Commands.Finish();
    }

    private static double? ReadTimer(WorkloadSlot slot) => slot.Timer.Supported ? slot.Timer.ReadMilliseconds() : null;

    private static async Task<double?> RetireAsync(WorkloadSlot slot)
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

    private static async Task<string> ReadPixelsAsync(VulkanDevice device, WorkloadSlot slot)
    {
        const uint Pitch = 5120;
        using IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new() { Count = Pitch * 720UL, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        using IGraphicsCommandBuffer copy = device.CreateCommandBuffer(new());
        copy.Barrier(new TextureBarrierDesc { Texture = slot.Texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.CopySource, Before = new(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        copy.CopyTextureToBuffer(new() { Texture = slot.Texture, Width = 1280, Height = 720 }, new() { Buffer = readback.SliceElements(0, readback.Count), BytesPerRow = Pitch, RowsPerImage = 720 });
        copy.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.SliceElements(0, readback.Count), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        copy.Finish();
        using IGraphicsSubmission submitted = device.Queue.Submit([copy]);
        await submitted.WaitAsync();
        await readback.MapAsync();
        byte[] data = new byte[(int)readback.Count];
        readback.CopyTo(data);
        readback.Unmap();
        if (!data.Any(b => b != 0))
        {
            throw new InvalidOperationException("Workload produced an empty target.");
        }

        return Convert.ToHexString(SHA256.HashData(data));
    }

    private static long Counter(IRenderingWorkload workload, string name) => workload.Counters.TryGetValue(name, out long value) ? value : 0;

    private static double Ms(long start) => Stopwatch.GetElapsedTime(start).TotalMilliseconds;

    private static void Emit(object value) => Console.WriteLine(JsonSerializer.Serialize(value));

    private sealed class WorkloadSlot : IDisposable
    {
        internal WorkloadSlot(VulkanDevice device)
        {
            Texture = device.CreateTexture(new() { Width = 1280, Height = 720, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
            View = Texture.CreateView();
            Timer = new(device);
        }

        internal IGraphicsTexture Texture { get; }

        internal IGraphicsTextureView View { get; }

        internal HardwareGpuTimer Timer { get; }

        internal List<PreparedDraw2D> Prepared { get; } = [];

        internal IReadOnlyList<DrawList2D>? Roots { get; set; }

        internal IGraphicsCommandBuffer? Commands { get; set; }

        internal IGraphicsSubmission? Submission { get; set; }

        internal bool Initialized { get; set; }

        public void Dispose()
        {
            if (Submission is not null)
            {
                throw new InvalidOperationException("Drain GPU work before slot disposal.");
            }

            Commands?.Dispose();
            foreach (PreparedDraw2D prepared in Prepared)
            {
                prepared.Dispose();
            }

            Timer.Dispose();
            View.Dispose();
            Texture.Dispose();
        }

        internal void ReleaseCompleted()
        {
            Submission!.Dispose();
            Commands!.Dispose();
            foreach (PreparedDraw2D prepared in Prepared)
            {
                prepared.Dispose();
            }

            Prepared.Clear();
            Roots = null;
            Commands = null;
            Submission = null;
        }
    }

    private sealed record Options(string Mode, string Suite, string? Case, int Count, int Warmup, int Samples, int Depth, bool Pipeline, bool Quick, string Artifacts)
    {
        internal static Options Parse(string[] args)
        {
            string? mode = null;
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            bool quick = false;
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg is "--workload-list" or "--workload-smoke" or "--workload-cpu" or "--workload-hardware")
                {
                    if (mode is not null)
                    {
                        throw new ArgumentException("Select exactly one workload command.");
                    }

                    mode = arg;
                }
                else if (arg == "--quick")
                {
                    quick = true;
                }
                else if (arg is "--suite" or "--case" or "--count" or "--warmup" or "--samples" or "--frames-in-flight" or "--mode" or "--artifacts")
                {
                    if (++i >= args.Length || !values.TryAdd(arg, args[i]))
                    {
                        throw new ArgumentException($"Missing or duplicate option: {arg}");
                    }
                }
                else
                {
                    throw new ArgumentException($"Unknown option: {arg}");
                }
            }

            int Number(string key, int fallback, int min, int max)
            {
                int value = values.TryGetValue(key, out string? s) ? int.Parse(s, System.Globalization.CultureInfo.InvariantCulture) : fallback;
                if (value < min || value > max)
                {
                    throw new ArgumentOutOfRangeException(key);
                }

                return value;
            }

            string renderMode = values.GetValueOrDefault("--mode", "stages");
            if (renderMode is not ("stages" or "pipeline"))
            {
                throw new ArgumentException("Mode must be stages or pipeline.");
            }

            int depth = Number("--frames-in-flight", 1, 1, 3);
            if (renderMode == "stages" && depth != 1)
            {
                throw new ArgumentException("Stages mode requires one frame in flight.");
            }

            return new(mode ?? throw new ArgumentException("Missing workload command."), values.GetValueOrDefault("--suite", "all"), values.GetValueOrDefault("--case"), Number("--count", 200, 1, 100000), Number("--warmup", 41, 0, 1000), Number("--samples", 120, 1, 10000), depth, renderMode == "pipeline", quick, values.GetValueOrDefault("--artifacts", "artifacts/rendering-workloads/bdn"));
        }
    }
}
