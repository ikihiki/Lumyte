using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lumyte.Graphics.BenchmarkWorkloads;
using Lumyte.Graphics.Gpu.Benchmarks;
using Lumyte.Graphics.Incremental.Benchmarks;
using Lumyte.Graphics.Vulkan;

if (args.Contains("--scenario", StringComparer.Ordinal))
{
    try
    {
        return await MutationRunner.RunAsync(args);
    }
    catch (Exception error) when (error is ArgumentException or FormatException or OverflowException)
    {
        Console.Error.WriteLine(error.Message);
        return 2;
    }
}

var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false };
Options options;
try
{
    options = Options.Parse(args);
}
catch (Exception error) when (error is ArgumentException or FormatException or OverflowException)
{
    Console.Error.WriteLine(error.Message);
    return 2;
}

Directory.CreateDirectory(options.Output);
string runId = Guid.NewGuid().ToString("N");
string commit = Git("rev-parse", "HEAD"), dirty = Git("diff", "HEAD");
string untracked = Git("ls-files", "--others", "--exclude-standard");
long initialized = Stopwatch.GetTimestamp();
VulkanDevice? device = null;
GpuRunner? runner = null;
try
{
    device = VulkanDevice.Create();
    double deviceInitializationMs = Stopwatch.GetElapsedTime(initialized).TotalMilliseconds;
    using var identity = new HardwareGpuTimer(device);
    var manifest = new
    {
        schema = "graphics-benchmarks-v1",
        runId,
        sourceCommit = commit.Trim(),
        dirtyDiffSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dirty))),
        dirtyDiff = dirty,
        untrackedPaths = untracked,
        contentProvenance = Provenance.Capture(),
        scenarioVersion = RectangleTrace.Version,
        options,
        seed = 27,
        caseId = options.Update == "static" ? "D01" : "D02",
        inputIds = new[] { new RectangleTrace(options.Update, options.Locality).InputId(0), new RectangleTrace(options.Update, options.Locality).InputId(1) },
        device = identity.DeviceDescription,
        backend = "Vulkan; Radeon 8060S guard inherited from HardwareGpuTimer",
        timestampSupported = identity.Supported,
        deviceInitializationMs,
        target = new { width = GpuRunner.Width, height = GpuRunner.Height, format = "Rgba8Srgb", dpr = 1, aaBlendFilter = "product defaults" },
        tree = "flat siblings; all 4096 elements visible; no snapshots retained",
        runtime = RuntimeInformation.FrameworkDescription,
        os = RuntimeInformation.OSDescription,
        architecture = RuntimeInformation.ProcessArchitecture.ToString(),
        cpu = File.Exists("/proc/cpuinfo") ? File.ReadLines("/proc/cpuinfo").FirstOrDefault(l => l.StartsWith("model name", StringComparison.Ordinal)) : null,
        serverGc = System.Runtime.GCSettings.IsServerGC,
        environment = new[] { "DOTNET_TieredCompilation", "DOTNET_TieredPGO", "DOTNET_ReadyToRun", "DOTNET_gcServer", "COMPlus_TieredCompilation", "COMPlus_TieredPGO", "VK_DRIVER_FILES" }.ToDictionary(k => k, Environment.GetEnvironmentVariable),
        coldContract = "fresh process required externally; first measured renderer Prepare; driver disk cache unknown; cold checks run after measurement",
        cache = new { maxCachedAssets = 512, maxCachedBytes = 67108864, maxFramesInFlight = 3 },
        counters = "drawCalls: forwarded real API calls; compile/reuse: public RetentionStatistics deltas around Prepare/Encode; cached bytes: estimated idle assets, not VRAM",
        boundaries = "recordPublish: Canvas Begin/End and ReplaceContent; prepare: borrow + awaited Prepare; encode: commands/barrier/Record/Finish; submit: queue call; hostFrame: Apply through submit + AttachSubmission; slotWait includes prior slot completion and retirement; completionLatency: submit start to observed fence, not GPU-only; gpuSpan: top/bottom timestamps around Record including clear/draw, excludes initial barrier and Prepare uploads; elapsed includes final drain/retirement and sampling bookkeeping",
        unavailableCounters = new { uploadBytes = (long?)null, evictionCount = (long?)null, nativeVram = (long?)null, shaderHash = (string?)null },
    };
    File.WriteAllText(Path.Combine(options.Output, "manifest.json"), JsonSerializer.Serialize(manifest, json));
    runner = new(device, options);
    (FrameSample[] Frames, double ElapsedMs) result = await runner.RunAsync();
    File.WriteAllLines(Path.Combine(options.Output, "frames.jsonl"), result.Frames.Select(sample => JsonSerializer.Serialize(new { runId, caseId = options.Update == "static" ? "D01" : "D02", profile = options, sample }, json)));
    File.WriteAllLines(Path.Combine(options.Output, "checks.jsonl"), runner.Checks.Select(check => JsonSerializer.Serialize(new { runId, check }, json)));
    double[] host = result.Frames.Select(f => f.HostFrameMs).ToArray();
    double[] gpu = result.Frames.Where(f => f.GpuSpanMs.HasValue).Select(f => f.GpuSpanMs!.Value).ToArray();
    var pilot = Enumerable.Range(0, Math.Max(0, (result.Frames.Length - 200) / 50) + 1).Select(i => 200 + (i * 50))
        .Where(n => n <= result.Frames.Length && n % 50 == 0)
        .Select(n => new { frames = n, hostStable = Stable(host.Take(n).ToArray()), gpuStable = gpu.Length == host.Length ? (bool?)Stable(gpu.Take(n).ToArray()) : null }).ToArray();
    int? recommendation = pilot.FirstOrDefault(p => p.hostStable && p.gpuStable != false)?.frames;
    var summary = new
    {
        runId,
        status = "passed",
        performanceUse = options.Phase == "formal" ? "formal single-process observation; requires independent alternating process pairs" : "execution/pilot/cold evidence; not formal comparison",
        frames = result.Frames.Length,
        finalDrainIncluded = true,
        elapsedMs = result.ElapsedMs,
        headlessFramesPerSecond = result.Frames.Length * 1000 / result.ElapsedMs,
        host = Distribution(host),
        gpu = gpu.Length > 0 ? Distribution(gpu) : null,
        pilot = options.Phase == "pilot" ? new { recommendedWarmup = recommendation, status = recommendation.HasValue ? "stable" : "unstable", windows = pilot } : null,
        minimumTenSecondMeasuredDuration = result.ElapsedMs >= 10000,
        recommendedMeasuredFrames = Math.Max(2000, (int)Math.Ceiling(result.Frames.Length * 10000 / result.ElapsedMs)),
    };
    File.WriteAllText(Path.Combine(options.Output, "summary.json"), JsonSerializer.Serialize(summary, json));
    Console.WriteLine(options.Output);
    return 0;
}
catch (Exception error)
{
    File.WriteAllText(Path.Combine(options.Output, "summary.json"), JsonSerializer.Serialize(new { runId, status = "failed", error = error.ToString(), comparisonValid = false }, json));
    File.WriteAllLines(Path.Combine(options.Output, "checks.jsonl"), (runner?.Checks ?? []).Select(check => JsonSerializer.Serialize(check, json)).Append(JsonSerializer.Serialize(new { status = "failed", error = error.Message }, json)));
    Console.Error.WriteLine(error);
    return 1;
}
finally
{
    if (PublicRetainedApiProbe.QuarantinedObjects.Count == 0)
    {
        device?.Dispose();
    }
    else if (device is not null)
    {
        PublicRetainedApiProbe.QuarantinedObjects.Add(device);
    }
}

static string Git(params string[] arguments)
{
    var start = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (string argument in arguments)
    {
        start.ArgumentList.Add(argument);
    }

    using Process process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start git.");
    string output = process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    return process.ExitCode == 0 ? output : "unknown: " + process.StandardError.ReadToEnd();
}

static object Distribution(double[] values)
{
    double[] sorted = values.Order().ToArray();
    return new { count = sorted.Length, medianMs = sorted[sorted.Length / 2], p95Ms = sorted[(int)Math.Ceiling(sorted.Length * 0.95) - 1], maximumMs = sorted[^1], over60Hz = sorted.Count(v => v > 1000.0 / 60), over120Hz = sorted.Count(v => v > 1000.0 / 120) };
}

static bool Stable(double[] values)
{
    if (values.Length < 200)
    {
        return false;
    }

    double[] medians = Enumerable.Range(0, 4).Select(i => values.Skip(values.Length - 200 + (i * 50)).Take(50).Order().ElementAt(25)).ToArray();
    return medians.Min() > 0 && medians.Max() / medians.Min() <= 1.05;
}
