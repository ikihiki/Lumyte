using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Samples;
using Lumyte.Graphics.Vulkan;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Primitives.Benchmarks;

/// <summary>Runs bounded primitive diagnostics without changing product rendering contracts.</summary>
internal static class Program
{
    internal const uint Size = 32;
    internal static readonly List<object> Frames = new();
    internal static readonly List<object> Checks = new();
    internal static readonly List<object> Summaries = new();
    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
    };

    internal static readonly List<object> Quarantine = new();

    /// <summary>Runs the bounded GPU fixtures and writes their diagnostic artifacts.</summary>
    /// <param name="args">The output path, case filter and bounded frame counts.</param>
    /// <returns>Zero after semantic success; otherwise one.</returns>
    public static async Task<int> Main(string[] args)
    {
        try
        {
            return await RunAsync(args);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new { status = "failed", error = error.ToString() }));
            return 1;
        }
    }

    internal static double Ms(long start) => Stopwatch.GetElapsedTime(start).TotalMilliseconds;

    internal static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    internal static void Require(bool valid, string message)
    {
        if (!valid)
        {
            throw new InvalidOperationException(message);
        }
    }

    internal static void Transition(IGraphicsCommandBuffer c, IGraphicsTexture texture, TextureState before, TextureState after, BarrierScope b, BarrierScope a) => c.Barrier(new TextureBarrierDesc
    {
        Texture = texture,
        Range = new(0, 1, 0, 1),
        BeforeState = before,
        AfterState = after,
        Before = b,
        After = a,
    });

    private static async Task<int> RunAsync(string[] args)
    {
        if (args.Contains("--recovery", StringComparer.Ordinal))
        {
            return await RecoveryRunner.RunAsync(args);
        }

        ValidateOptions(args);
        string output = Option(args, "--output", "artifacts/graphics-benchmarks/primitives-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        int count = int.Parse(Option(args, "--frames", "12"), System.Globalization.CultureInfo.InvariantCulture);
        int warmup = int.Parse(Option(args, "--warmup", "3"), System.Globalization.CultureInfo.InvariantCulture);
        string filter = Option(args, "--case", "all");
        Require(count > 0 && count <= 100000 && warmup >= 0 && warmup <= 2000, "Frames must be 1..100000 and warmup 0..2000.");
        Require(filter == "all" || Catalog().Any(spec => spec.Id == filter), "Choose --case all, G01, G02, G05 or G06.");
        Directory.CreateDirectory(output);
        VulkanDevice? device = null;
        object? identity = null;
        string status = "passed";
        string? error = null;
        try
        {
            device = VulkanDevice.Create();
            identity = DeviceIdentity(device);

            // Preserve the existing sample as an explicit diagnostic rather than copying it.
            if (filter is "all" or "G02")
            {
                PipelineBenchmarkResult sample = await PipelineBenchmark.RunAsync(device, 24);
                Checks.Add(new
                {
                    check = "existing-pipeline-sample",
                    status = "passed",
                    result = sample,
                    semanticValidation = "separate analytic fixtures below",
                });
            }

            foreach (Case spec in Catalog().Where(c => filter == "all" || c.Id == filter))
            {
                foreach (int depth in new[]
                {
                    1,
                    3,
                })
                {
                    await RunCaseAsync(device, spec, depth, warmup, count, output);
                }
            }

            Require(Summaries.Count > 0, "No matching cases.");
            Checks.Add(new
            {
                check = "device-loss-recovery",
                status = "not-measured",
                reason = "Ordinary warm-frame run excludes recovery. Use a separate fresh-process --recovery invocation for native WebGPU device destruction and Hosting recovery.",
            });
        }
        catch (Exception ex)
        {
            status = "failed";
            error = ex.ToString();
            Checks.Add(new
            {
                check = "run",
                status,
                error,
            });
        }
        finally
        {
            // Unknown submission acceptance pins device AND every resource until process termination.
            if (Quarantine.Count == 0)
            {
                device?.Dispose();
            }
            else
            {
                Quarantine.Add(device!);
            }
        }

        Dictionary<string, string?> sources = new[]
        {
            "Program.cs",
            "Slot.cs",
            "Lumyte.Graphics.Primitives.Benchmarks.csproj",
        }.ToDictionary(x => x, x => File.Exists(Path.Combine(AppContext.BaseDirectory, x)) ? Hash(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, x))) : (string?)null);
        var manifest = new
        {
            schema = "graphics-primitives/1",
            scenarioVersion = "adr27-primitives-bounded-v1",
            status,
            error,
            runId = Path.GetFileName(output),
            sourceCommit = Git("rev-parse HEAD"),
            provenance = Lumyte.Graphics.Gpu.Benchmarks.Provenance.Capture(),
            dirtyDiffSha256 = Hash(System.Text.Encoding.UTF8.GetBytes(Git("diff --binary HEAD"))),
            inputSha256 = Hash(JsonSerializer.SerializeToUtf8Bytes(Catalog(), Json)),
            seed = 0,
            actualDevice = identity,
            os = RuntimeInformation.OSDescription,
            cpu = File.Exists("/proc/cpuinfo") ? File.ReadLines("/proc/cpuinfo").FirstOrDefault(x => x.StartsWith("model name")) : null,
            runtime = RuntimeInformation.FrameworkDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            jit = "product-default; no harness overrides",
            environment = new[]
            {
                "DOTNET_TieredCompilation",
                "DOTNET_TieredPGO",
                "DOTNET_ReadyToRun",
                "DOTNET_gcServer",
                "VK_DRIVER_FILES",
                "VK_ICD_FILENAMES",
            }.ToDictionary(x => x, Environment.GetEnvironmentVariable),
            target = new
            {
                width = Size,
                height = Size,
                format = "Rgba8Unorm",
                dpr = 1,
                reason = "bounded diagnostic; not standard 1920x1080 Rgba8Srgb",
                aa = "product-default",
                filtering = "product-default",
            },
            frames = count,
            warmup,
            sourceHashes = sources,
            measurementVersion = "primitive-queue-timestamps-v2",
            gpuSpan = "slot-owned timestamp pair across ordered same-queue commands: initial barriers, upload, clear and draw included; readback excluded; separate-submit queue starvation may be included; not summed busy time",
            gpuSpanReason = "Actual queue capability is recorded; unsupported timestamps stay null; every supported pair must be finite/nonnegative after all submissions complete and before slot reuse.",
            nativeMemory = (long?)null,
            gpuMemory = (long?)null,
            counters = "actual harness-forwarded Draw/ModelMesh.Draw, command and Submit calls; not GPU primitive counters",
            boundaries = "prepare includes asset replacement; encode includes commands, barriers, clear, binding and Finish; submit host call; completion CPU wall; throughput includes final drain/retirement but excludes image verification and JSON",
            gaps = new[]
            {
                "G02 many-state variants absent; grouped/alternating two raster states only",
                "G05 no textures/material diversity, culling, LOD or indirect",
                "G06 ordinary run covers raw mesh replacement only; native recovery is a separate --recovery process; ResourceManager/file decode and in-place mutation remain unmeasured",
                "cold means fresh native program object, not cold driver disk cache",
                "bounded trace is not formal 2000-frame/5-process A/B or stability pilot",
            },
            quarantinedOwners = Quarantine.Count,
        };
        File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(manifest, Json));
        File.WriteAllLines(Path.Combine(output, "frames.jsonl"), Frames.Select(x => JsonSerializer.Serialize(x, Json)));
        File.WriteAllLines(Path.Combine(output, "checks.jsonl"), Checks.Select(x => JsonSerializer.Serialize(x, Json)));
        var summary = new
        {
            status,
            cases = Summaries,
        };
        File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(summary, Json));
        Console.WriteLine(output + ": " + status);
        return status == "passed" ? 0 : 1;
    }

    private static void ValidateOptions(string[] args)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < args.Length; index++)
        {
            string option = args[index];
            if (option is not ("--output" or "--frames" or "--warmup" or "--case"))
            {
                throw new ArgumentException($"Unknown option: {option}.");
            }

            if (!seen.Add(option))
            {
                throw new ArgumentException($"Duplicate option: {option}.");
            }

            if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]) || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Missing value for {option}.");
            }

            index++;
        }
    }

    private static string Option(string[] args, string key, string fallback)
    {
        int i = Array.IndexOf(args, key);
        return i < 0 ? fallback : args[i + 1];
    }

    private static string Git(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("git", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        });
        if (process is null)
        {
            return "unknown";
        }

        string result = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return result.Trim();
    }

    private static unsafe object DeviceIdentity(VulkanDevice device)
    {
        T Read<T>(string name) => (T)typeof(VulkanDevice).GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(device)!;
        Vk api = Read<Vk>("Api");
        var driver = new PhysicalDeviceDriverProperties
        {
            SType = StructureType.PhysicalDeviceDriverProperties,
        };
        var props = new PhysicalDeviceProperties2
        {
            SType = StructureType.PhysicalDeviceProperties2,
            PNext = &driver,
        };
        api.GetPhysicalDeviceProperties2(Read<PhysicalDevice>("PhysicalDevice"), &props);
        PhysicalDeviceProperties p = props.Properties;
        PhysicalDevice physical = Read<PhysicalDevice>("PhysicalDevice");
        uint queueFamily = Read<uint>("QueueFamily");
        uint familyCount = 0;
        api.GetPhysicalDeviceQueueFamilyProperties(physical, &familyCount, null);
        var families = new QueueFamilyProperties[familyCount];
        fixed (QueueFamilyProperties* pointer = families)
        {
            api.GetPhysicalDeviceQueueFamilyProperties(physical, &familyCount, pointer);
        }

        uint? timestampValidBits = queueFamily < familyCount ? families[queueFamily].TimestampValidBits : null;
        return new
        {
            name = Marshal.PtrToStringUTF8((nint)p.DeviceName),
            vendor = p.VendorID,
            device = p.DeviceID,
            type = p.DeviceType.ToString(),
            driver = Marshal.PtrToStringUTF8((nint)driver.DriverName),
            driverInfo = Marshal.PtrToStringUTF8((nint)driver.DriverInfo),
            driverVersion = p.DriverVersion,
            backend = "Vulkan",
            caps = device.Caps,
            queueFamily,
            timestampPeriodNs = p.Limits.TimestampPeriod,
            timestampValidBits,
            timestampInstrumented = timestampValidBits is > 0 and <= 64 && p.Limits.TimestampPeriod > 0,
        };
    }

    private static Case[] Catalog() =>
    [
        new("G01", "draw24-cb1-submit1", 24, 1, 1),
        new("G01", "draw24-cb4-submit1", 24, 4, 1),
        new("G01", "draw24-cb4-submit4", 24, 4, 4),
        new("G02", "cold-program", 24, 1, 1, Cold: true),
        new("G02", "warm-same", 24, 1, 1),
        new("G02", "warm-grouped-states", 24, 1, 1, States: "grouped"),
        new("G02", "warm-alternating-states", 24, 1, 1, States: "alternating"),
        new("G05", "fixed-draw4-tri1-reused", 4, Model: true),
        new("G05", "fixed-draw4-tri2-reused", 4, Model: true, Triangles: 2),
        new("G05", "fixed-vertex24-draw8-reused", 8, Model: true),
        new("G05", "fixed-vertex24-draw8-unique", 8, Model: true, Unique: true),
        new("G05", "fixed-vertex24-draw8-transparent", 8, Model: true, Transparent: true),
        new("G06", "asset-create-update-retire", 4, Model: true, Unique: true, Replace: true),
    ];

    private static async Task RunCaseAsync(VulkanDevice device, Case spec, int depth, int warmup, int count, string output)
    {
        var slots = new Slot[depth];
        for (int i = 0; i < depth; i++)
        {
            slots[i] = await Slot.CreateAsync(device, spec);
        }

        var samples = new List<double>(count);
        var rows = new FrameSample[count];
        double[] completions = new double[count];
        double?[] gpuSamples = new double?[count];
        foreach (Slot slot in slots)
        {
            slot.CompletionSamples = completions;
            slot.GpuSamples = gpuSamples;
        }

        bool safe = true;
        try
        {
            // First/mutated/last image checks are outside timing. Each depth is independently checked.
            foreach (int frame in new[]
            {
                0,
                1,
                count - 1,
            }.Distinct())
            {
                Slot slot = slots[frame % depth];
                await slot.PrepareAsync(frame);
                slot.Encode(frame);
                slot.Submit();
                await slot.CompleteAsync();
                byte[] pixels = await slot.ReadPixelsAsync();
                CheckPixels(spec, frame, pixels);
                string filename = spec.Id + "-" + spec.Name + "-d" + depth + "-f" + frame + ".rgba";
                File.WriteAllBytes(Path.Combine(output, filename), pixels);
                Checks.Add(new
                {
                    check = "analytic-all-pixels",
                    status = "passed",
                    spec.Id,
                    spec.Name,
                    depth,
                    frame,
                    sha256 = Hash(pixels),
                    image = filename,
                    reference = "independent analytic RGBA per pixel, integer UNORM rounding <=1",
                });
                slot.ReleaseFrame();
            }

            for (int frame = 0; frame < warmup; frame++)
            {
                Slot slot = slots[frame % depth];
                await slot.CompleteAsync();
                slot.ReleaseFrame();
                await slot.PrepareAsync(frame);
                slot.Encode(frame);
                slot.Submit();
            }

            foreach (Slot slot in slots)
            {
                await slot.CompleteAsync();
                slot.ReleaseFrame();
            }

            long window = Stopwatch.GetTimestamp();
            for (int frame = 0; frame < count; frame++)
            {
                Slot slot = slots[frame % depth];
                long outer = Stopwatch.GetTimestamp();
                long t = outer;
                await slot.CompleteAsync();
                double wait = Ms(t);
                t = Stopwatch.GetTimestamp();
                slot.ReleaseFrame();
                double retirement = Ms(t);
                long host = Stopwatch.GetTimestamp();
                t = host;
                await slot.PrepareAsync(frame);
                double prepare = Ms(t);
                t = Stopwatch.GetTimestamp();
                slot.Encode(frame);
                double encode = Ms(t);
                t = Stopwatch.GetTimestamp();
                slot.Submit();
                double submit = Ms(t);
                double hostFrame = Ms(host);
                samples.Add(hostFrame);
                rows[frame] = new(prepare, encode, submit, hostFrame, wait, retirement, Ms(outer), slot.DrawCalls, slot.CommandCalls, slot.SubmitCalls, slot.ShaderHash);
                slot.Frame = frame;
            }

            foreach (Slot slot in slots)
            {
                await slot.CompleteAsync();
                slot.ReleaseFrame();
            }

            double elapsed = Ms(window);
            for (int frame = 0; frame < count; frame++)
            {
                FrameSample row = rows[frame];
                Frames.Add(new
                {
                    schema = "graphics-primitives/1",
                    spec.Id,
                    spec.Name,
                    depth,
                    frame,
                    inputId = spec.Name + ":" + frame,
                    phase = spec.Cold || spec.Replace ? "cold-asset" : "warm",
                    prepareMs = row.Prepare,
                    encodeMs = row.Encode,
                    submitMs = row.Submit,
                    hostFrameMs = row.Host,
                    slotWaitMs = row.Wait,
                    retirementMs = row.Retirement,
                    outerHostMs = row.Outer,
                    completionLatencyMs = completions[frame],
                    gpuSpanMs = gpuSamples[frame],
                    gpuQueryStatus = gpuSamples[frame].HasValue ? "valid" : "unsupported",
                    draws = row.Draws,
                    commands = row.Commands,
                    submits = row.Submits,
                    indexedVertices = spec.Draws * spec.Triangles * 3,
                    shaderSha256 = row.Shader,
                    memoryBytes = (long?)null,
                });
            }

            // Validate real pipelined outputs after the final drain, not just serialized preflight.
            for (int index = 0; index < depth; index++)
            {
                int last = count - 1 - ((count - 1 - index + depth) % depth);
                if (last < 0)
                {
                    continue;
                }

                byte[] pixels = await slots[index].ReadPixelsAsync();
                CheckPixels(spec, last, pixels);
                slots[index].ReleaseFrame();
                Checks.Add(new
                {
                    check = "pipelined-final-pixels",
                    status = "passed",
                    spec.Id,
                    spec.Name,
                    depth,
                    frame = last,
                    sha256 = Hash(pixels),
                });
            }

            samples.Sort();
            double[] gpuOrdered = gpuSamples.Where(value => value.HasValue).Select(value => value!.Value).Order().ToArray();
            Summaries.Add(new
            {
                spec.Id,
                spec.Name,
                depth,
                count,
                elapsedIncludingFinalDrainMs = elapsed,
                framesPerSecond = count * 1000 / elapsed,
                hostMedianMs = samples.Count % 2 == 0 ? (samples[(samples.Count / 2) - 1] + samples[samples.Count / 2]) / 2 : samples[samples.Count / 2],
                hostP95Ms = samples[(int)Math.Ceiling(samples.Count * .95) - 1],
                status = "diagnostic-only",
                gpu = gpuOrdered.Length == 0 ? null : new
                {
                    count = gpuOrdered.Length,
                    medianMs = gpuOrdered.Length % 2 == 0 ? (gpuOrdered[(gpuOrdered.Length / 2) - 1] + gpuOrdered[gpuOrdered.Length / 2]) / 2 : gpuOrdered[gpuOrdered.Length / 2],
                    p95Ms = gpuOrdered[(int)Math.Ceiling(gpuOrdered.Length * 0.95) - 1],
                    maximumMs = gpuOrdered[^1],
                },
            });
        }
        catch
        {
            safe = false;
            Quarantine.Add(slots);
            throw;
        }
        finally
        {
            if (safe)
            {
                foreach (Slot slot in slots)
                {
                    slot.Dispose();
                }
            }
        }
    }

    private static void CheckPixels(Case spec, int frame, byte[] pixels)
    {
        bool green = spec.Replace && frame % 2 == 1;
        double alpha = spec.Transparent ? 1 - Math.Pow(.5, spec.Draws) : spec.Model ? 1 : .5;

        // Straight-alpha blending of constant red over transparent black produces premultiplied RGB.
        int r = green ? 0 : spec.Transparent ? (int)Math.Round(alpha * 255) : 255;
        int g = green ? 255 : 0;
        int a = (int)Math.Round(alpha * 255);
        for (int i = 0; i < pixels.Length; i += 4)
        {
            Require(Math.Abs(pixels[i] - r) <= 1 && Math.Abs(pixels[i + 1] - g) <= 1 && pixels[i + 2] == 0 && Math.Abs(pixels[i + 3] - a) <= 1, "Analytic reference mismatch: " + spec.Name + " frame=" + frame + " byte=" + i + " actual=" + string.Join(',', pixels.Skip(i).Take(4)));
        }
    }

    private readonly record struct FrameSample(double Prepare, double Encode, double Submit, double Host, double Wait, double Retirement, double Outer, int Draws, int Commands, int Submits, string Shader);

    internal sealed record Case(string Id, string Name, int Draws, int Commands = 1, int Submits = 1, bool Cold = false, string States = "same", bool Model = false, int Triangles = 1, bool Unique = false, bool Transparent = false, bool Replace = false);
}
