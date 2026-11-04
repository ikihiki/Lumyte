using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.Vulkan;

namespace Lumyte.Graphics.Effects.Benchmarks;

internal static class EffectRunner
{
    internal static async Task RunAsync(string[] args)
    {
        string mode = CliOptions.Validate(args);
        bool smoke = mode == "--smoke";
        if (mode == "--list")
        {
            foreach (EffectProfile profile in EffectProfile.All)
            {
                Console.WriteLine(JsonSerializer.Serialize(profile));
            }

            return;
        }

        if (!smoke && mode != "--run")
        {
            throw new ArgumentException("Choose --list, --smoke, or --run --profile ID --source COMMIT [--depth 1|3 --warmup 200 --frames 2000 --output PATH].");
        }

        string source = Value(args, "--source", smoke ? "unmeasured-smoke" : string.Empty);
        if (string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentException("Measured runs require --source COMMIT (also record dirty diff beside artifacts).");
        }

        int depth = Number(args, "--depth", 1, 1, 3);
        if (depth is not (1 or 3))
        {
            throw new ArgumentException("Only depth 1 or 3 is supported.");
        }

        int warmup = Number(args, "--warmup", 200, 0, 2000);
        int frames = Number(args, "--frames", 2000, 1, 100000);
        string requested = Value(args, "--profile", smoke ? "all" : string.Empty);
        EffectProfile[] profiles = requested == "all" && smoke ? EffectProfile.All.ToArray() : [EffectProfile.All.Single(profile => profile.Id == requested)];
        string output = Value(args, "--output", $"artifacts/graphics-benchmarks/effects-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Environment.ProcessId}");
        Directory.CreateDirectory(output);
        foreach (EffectProfile standard in profiles)
        {
            // Small checks preserve topology, effects, ordering and sigma. They are not timing profiles.
            EffectProfile profile = smoke ? standard with { Width = 64, Height = 64 } : standard;
            byte[]? crossDepthReference = null;
            foreach (int selectedDepth in smoke ? new[] { 1, 3 } : new[] { depth })
            {
                crossDepthReference = await RunProfileAsync(profile, selectedDepth, smoke ? 0 : warmup, smoke ? 3 : frames, smoke, source, output, crossDepthReference);
            }
        }
    }

    private static async Task<byte[]> RunProfileAsync(EffectProfile profile, int depth, int warmup, int count, bool smoke, string source, string output, byte[]? crossDepthReference)
    {
        string prefix = Path.Combine(output, $"{profile.Id}-depth{depth}");
        if (File.Exists(prefix + "-manifest.json"))
        {
            throw new IOException("Output profile/depth already exists; choose a fresh output directory.");
        }

        long deviceStarted = Stopwatch.GetTimestamp();
        using var device = VulkanDevice.Create();
        double deviceInitializationMs = Stopwatch.GetElapsedTime(deviceStarted).TotalMilliseconds;
        var description = new TextureDesc
        {
            Width = checked((uint)profile.Width),
            Height = checked((uint)profile.Height),
            Format = TextureFormat.Rgba16Float,
            Usage = TextureUsage.RenderAttachment | TextureUsage.Sampled | TextureUsage.CopySource,
        };
        RenderGraphDrawList2D list = profile.Record();
        var slots = new List<EffectSlot>();
        var samples = new List<FrameSample>(count);
        try
        {
            for (int index = 0; index < depth; index++)
            {
                slots.Add(new EffectSlot(device, description) { Index = index });
            }

            await File.WriteAllTextAsync(prefix + "-manifest.json", JsonSerializer.Serialize(new
            {
                scenario = "D10-executed-layers-and-G04-record",
                version = 1,
                status = "configuration; inspect summary/failure for completion",
                source,
                provenance = Lumyte.Graphics.Gpu.Benchmarks.Provenance.Capture(),
                profile,
                deviceInitializationMs,
                coldScope = "first local device/graph execution is in before-check stages with diagnostic graph metadata enabled; fresh process required for cold observations, driver disk caches unknown",
                inputHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(profile)))),
                shaderHash = (string?)null,
                depth,
                warmup,
                measuredFrames = count,
                smoke,
                device = slots[0].Timer.DeviceDescription,
                runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"),
                icd = Environment.GetEnvironmentVariable("VK_DRIVER_FILES"),
                jitEnvironment = Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
                    .Where(entry => entry.Key.ToString()!.StartsWith("DOTNET_", StringComparison.Ordinal) || entry.Key.ToString()!.StartsWith("COMPlus_", StringComparison.Ordinal))
                    .ToDictionary(entry => entry.Key.ToString()!, entry => entry.Value?.ToString()),
                directScene = "N/A: direct Scene cannot execute layers/effects",
                format = "Rgba16Float linear premultiplied; DPR 1",
                seed = "none; fixed normalized rectangles and static logical frame",
                graphBuildPrepare = "public Append2D including resource preparation; NOT pure CPU graph declaration",
                gpuSpan = "whole graph including barriers/clear/effects/output export; excludes readback",
                observationLatency = "submit until explicit completion observation; includes deferred polling",
                stability = "not-assessed; choose/fix warmup through external pilot before formal comparison",
                oracleTolerance = AnalyticReference.AbsoluteTolerance,
                softwareGpuPerformance = "not valid; adapter identity above must be reviewed",
            }));
            byte[] reference = await CheckAsync(device, profile, description, list, slots, crossDepthReference, prefix, "before");
            for (int frameId = 0; frameId < warmup; frameId++)
            {
                EffectSlot slot = slots[frameId % depth];
                await slot.RetireAsync();
                slot.Frame = await EffectFrame.CreateAsync(device, slot.Texture, description, list, false);
                slot.Frame.Submit(device, slot.Timer);
            }

            // Warmup drain is explicitly outside measured throughput.
            foreach (EffectSlot slot in slots)
            {
                await slot.RetireAsync();
            }

            long start = Stopwatch.GetTimestamp();
            for (int frameId = 0; frameId < count; frameId++)
            {
                long host = Stopwatch.GetTimestamp();
                EffectSlot slot = slots[frameId % depth];
                FrameSample? prior = await slot.RetireAsync();
                if (prior is not null)
                {
                    samples.Add(prior);
                }

                slot.SlotWaitMs = slot.LastWaitMs;
                slot.FrameId = frameId;
                slot.Frame = await EffectFrame.CreateAsync(device, slot.Texture, description, list, false);
                slot.Frame.Submit(device, slot.Timer);
                slot.HostMs = Stopwatch.GetElapsedTime(host).TotalMilliseconds;
            }

            foreach (EffectSlot slot in slots)
            {
                FrameSample? sample = await slot.RetireAsync();
                if (sample is not null)
                {
                    samples.Add(sample);
                }
            }

            // Includes final GPU drain, command/plan/prepared/renderer/graph retirement.
            double elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (samples.Count != count || samples.Select(sample => sample.FrameId).Distinct().Count() != count)
            {
                throw new InvalidOperationException("Measured frame identities/count do not match the requested trace.");
            }

            if (samples.Any(sample => sample.Statistics != samples[0].Statistics))
            {
                throw new InvalidOperationException("Actual graph statistics changed during the fixed trace.");
            }

            await CheckAsync(device, profile, description, list, slots, reference, prefix, "after");
            await File.WriteAllLinesAsync(prefix + "-frames.jsonl", samples.OrderBy(sample => sample.FrameId).Select(sample => JsonSerializer.Serialize(sample)));
            await File.WriteAllTextAsync(prefix + "-summary.json", JsonSerializer.Serialize(new
            {
                status = "passed",
                smoke,
                count,
                depth,
                elapsedIncludingFinalDrainAndRetirementMs = elapsedMs,
                headlessFramesPerSecond = count * 1000 / elapsedMs,
                statistics = samples[0].Statistics,
                graphBuildPrepareMs = Distribution(samples.Select(sample => sample.GraphBuildPrepareMs)),
                graphCompileMs = Distribution(samples.Select(sample => sample.GraphCompileMs)),
                graphRecordMs = Distribution(samples.Select(sample => sample.GraphRecordMs)),
                encodeMs = Distribution(samples.Select(sample => sample.EncodeMs)),
                hostIncludingSlotWaitMs = Distribution(samples.Select(sample => sample.HostIncludingSlotWaitMs)),
                gpuSpanMs = Distribution(samples.Where(sample => sample.GpuSpanMs.HasValue).Select(sample => sample.GpuSpanMs!.Value)),
            }));
            Console.WriteLine(JsonSerializer.Serialize(new { profile.Id, depth, smoke, status = "passed", prefix }));
            return reference;
        }
        catch (Exception error)
        {
            await File.WriteAllTextAsync(prefix + "-failure.json", JsonSerializer.Serialize(new { status = "failed", error = error.ToString() }));
            throw;
        }
        finally
        {
            // Even after a later slot's build/Record failure, drain all earlier submitted frames.
            foreach (EffectSlot slot in slots)
            {
                await slot.RetireAsync();
            }

            foreach (EffectSlot slot in slots)
            {
                slot.Dispose();
            }
        }
    }

    private static async Task<byte[]> CheckAsync(VulkanDevice device, EffectProfile profile, TextureDesc description, RenderGraphDrawList2D list, List<EffectSlot> slots, byte[]? reference, string prefix, string phase)
    {
        string? metadata = null;
        foreach (EffectSlot slot in slots)
        {
            slot.Frame = await EffectFrame.CreateAsync(device, slot.Texture, description, list, true);
            RenderGraphSnapshot snapshot = slot.Frame.Snapshot ?? throw new InvalidOperationException("Check snapshot exceeded budget.");
            int effects = snapshot.Passes.Count(pass => pass.Name == "image-effect" && pass.ExecutionIndex >= 0);
            int expectedEffects = profile.Kind switch
            {
                "opaque" => 0,
                "isolation" => profile.Layers,
                "blur" => profile.Layers * 2,
                "backdrop" => 2,
                _ => throw new InvalidOperationException("Unknown effect profile."),
            };
            if (effects != expectedEffects || slot.Frame.Statistics.TransientResources < expectedEffects || slot.Frame.Statistics.CulledPasses != 0)
            {
                throw new InvalidOperationException("Required effect passes/resources were absent or culled.");
            }

            string current = JsonSerializer.Serialize(new { slot.Frame.Statistics, snapshot.Passes, snapshot.Resources, snapshot.Dependencies, snapshot.Barriers });
            metadata ??= current;
            if (metadata != current)
            {
                throw new InvalidOperationException("Graph semantics changed across frame slots.");
            }

            slot.Frame.Submit(device, slot.Timer);
        }

        foreach (EffectSlot slot in slots)
        {
            FrameSample sample = await slot.RetireAsync() ?? throw new InvalidOperationException("Missing check frame.");
            byte[] pixels = await PixelReadback.ReadAsync(device, slot.Texture);
            float maximumError = AnalyticReference.Verify(profile, pixels);
            string hash = Convert.ToHexString(SHA256.HashData(pixels));
            reference ??= pixels;
            if (!reference.AsSpan().SequenceEqual(pixels))
            {
                throw new InvalidOperationException("Exact same-path pixel determinism/slot reuse check failed.");
            }

            var checkRow = new
            {
                phase,
                slot = slot.Index,
                status = "passed",
                hash,
                analyticMaximumError = maximumError,
                tolerance = AnalyticReference.AbsoluteTolerance,
                sample.Statistics,
                firstExecutionStages = phase == "before" ? new
                {
                    sample.GraphBuildPrepareMs,
                    sample.GraphCompileMs,
                    sample.GraphRecordMs,
                    sample.EncodeMs,
                    sample.SubmitMs,
                    sample.SubmitToCompletionObservationMs,
                    sample.RetirementMs,
                    sample.GpuSpanMs,
                }

                : null,
                graphMetadata = metadata,
            };
            await File.AppendAllTextAsync(prefix + "-checks.jsonl", JsonSerializer.Serialize(checkRow) + Environment.NewLine);
        }

        return reference ?? throw new InvalidOperationException("No reference frame rendered.");
    }

    private static object? Distribution(IEnumerable<double> source)
    {
        double[] values = source.Order().ToArray();
        return values.Length == 0 ? null : new
        {
            median = values.Length % 2 == 0 ? (values[(values.Length / 2) - 1] + values[values.Length / 2]) / 2 : values[values.Length / 2],
            p95 = values[(int)Math.Ceiling(values.Length * 0.95) - 1],
            maximum = values[^1],
            samples = values.Length,
        };
    }

    private static string Value(string[] args, string key, string fallback)
    {
        int index = Array.IndexOf(args, key);
        return index < 0 ? fallback : index + 1 < args.Length ? args[index + 1] : throw new ArgumentException($"Missing value for {key}.");
    }

    private static int Number(string[] args, string key, int fallback, int minimum, int maximum)
    {
        int value = int.Parse(Value(args, key, fallback.ToString(System.Globalization.CultureInfo.InvariantCulture)), System.Globalization.CultureInfo.InvariantCulture);
        return value >= minimum && value <= maximum ? value : throw new ArgumentOutOfRangeException(key);
    }
}
