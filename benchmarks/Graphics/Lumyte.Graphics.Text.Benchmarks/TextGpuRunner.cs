using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Vulkan;

namespace Lumyte.Graphics.Text.Benchmarks;

internal static class TextGpuRunner
{
    internal static async Task RunAsync(string[] args)
    {
        bool smoke = CliOptions.Validate(args) == "--smoke";
        string selectedCase = Value(args, "--case", smoke ? "all" : string.Empty);
        string selectedMethod = Value(args, "--method", smoke ? "all" : string.Empty);
        int depth = Number(args, "--depth", 1, 1, 3);
        TextChecks.Require(depth is 1 or 3, "Depth must be 1 or 3.");
        int frames = Number(args, "--frames", 2000, 1, 100000);
        int warmup = Number(args, "--warmup", 200, 0, 2000);
        string source = Value(args, "--source", smoke ? "unmeasured-smoke" : string.Empty);
        TextChecks.Require(!string.IsNullOrWhiteSpace(source), "Measured runs require --source COMMIT.");
        string output = Value(args, "--output", $"artifacts/graphics-benchmarks/text-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Environment.ProcessId}");
        TextCase[] cases = smoke && selectedCase == "all" ? TextCase.All.ToArray() : [TextCase.Find(selectedCase)];
        string[] methods = smoke && selectedMethod == "all" ? ["vector", "msdf", "mtsdf"] : [selectedMethod];
        TextChecks.Require(methods.All(method => method is "vector" or "msdf" or "mtsdf"), "Choose vector, msdf or mtsdf.");
        TextChecks.Require(File.Exists(TextCase.FontPath("DejaVuSans.ttf")), "Required DejaVu Latin/Arabic font asset is missing.");
        Directory.CreateDirectory(output);
        foreach (TextCase input in cases)
        {
            if (!input.Available)
            {
                string gap = JsonSerializer.Serialize(new { input.Id, status = "asset-gap", font = input.AdditionalFont, reason = "Explicit font file absent; not an unsupported public API." });
                Console.WriteLine(gap);
                await File.AppendAllTextAsync(Path.Combine(output, "asset-gaps.jsonl"), gap + Environment.NewLine);
                if (!smoke)
                {
                    throw new FileNotFoundException("Requested font asset is absent.");
                }

                continue;
            }

            object semantics = TextChecks.Verify(input);
            foreach (string method in methods)
            {
                byte[][]? crossDepth = null;
                foreach (int selectedDepth in smoke ? new[] { 1, 3 } : new[] { depth })
                {
                    crossDepth = await RunCaseAsync(input, method, selectedDepth, smoke ? 0 : warmup, smoke ? 4 : frames, source, output, smoke, semantics, crossDepth);
                }
            }
        }
    }

    private static async Task<byte[][]> RunCaseAsync(TextCase input, string method, int depth, int warmup, int count, string source, string output, bool smoke, object semantics, byte[][]? crossDepth)
    {
        string prefix = Path.Combine(output, $"{input.Id}-{method}-depth{depth}");
        TextChecks.Require(!File.Exists(prefix + "-manifest.json"), "Choose a fresh output directory; profile/method/depth already exists.");
        using var fixture = new TextFixture(input);
        using var device = VulkanDevice.Create();
        using TextAssets assets = await TextAssets.CreateAsync(device, fixture, method);
        var slots = new List<TextSlot>();
        var samples = new List<TextFrameSample>(count);
        try
        {
            for (int index = 0; index < depth; index++)
            {
                slots.Add(new TextSlot(device, index));
            }

            var manifest = new
            {
                scenario = "D07-text",
                version = 1,
                source,
                input,
                method,
                depth,
                warmup,
                frames = count,
                smoke,
                fonts = fixture.Fonts,
                inputHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(input)))),
                target = "640x320 Rgba16Float; white opaque clear; DPR1",
                fontSize = input.Id == "colr" ? 64 : 24,
                anchor = "DejaVu O at64px; (16,16); main origin (104,16)",
                width0 = input.Width(0),
                width1 = input.Width(1),
                atlas = "one texel/logical unit; range8; max2048; MSDF/MTSDF are quality variants, not pixel-equivalent vector implementations",
                mainColrDistanceScope = input.Id == "colr" ? "Color paint remains vector; only analytic O anchor uses a distance field." : "Main outlines use the selected representation.",
                shapingScope = "Parley public call combines shaping/layout/outline extraction/snapshot copy; provider case also constructs caret/range geometry",
                atlasCreateScope = "CreateAsync includes real CPU generation+packing, native texture/view/buffer allocation and staging map/copy; do not label it CPU-only atlas generation",
                residentScope = "two prepared logical input recordings and immutable uploaded atlas owners; per-frame renderer Prepare/Record/Submit/completion; no shaping or atlas rebuild in resident interval",
                firstUseScope = "per-state setup lifecycle, fresh local engine; no global native/driver cache-cold assertion or persistent glyph-cache-miss claim",
                qualityScope = "Analytic O counter/stroke + source/font/cluster semantics; byte-exact same-method repeats/slots/depth and vector logical-adapter reference. No vector-vs-DF pixel-equivalence assertion.",
                completionScope = "submit-to-completion observation includes deferred host polling",
                gpuScope = "target transitions+clear+text draws+CopySource export; no resident atlas upload/readback",
                device = slots[0].Timer.DeviceDescription,
                icd = Environment.GetEnvironmentVariable("VK_DRIVER_FILES"),
                runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"),
                jitEnvironment = Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
                    .Where(entry => entry.Key.ToString()!.StartsWith("DOTNET_", StringComparison.Ordinal) || entry.Key.ToString()!.StartsWith("COMPlus_", StringComparison.Ordinal))
                    .ToDictionary(entry => entry.Key.ToString()!, entry => entry.Value?.ToString()),
                shaderHash = (string?)null,
                stability = "not-assessed; external pilot required before formal comparisons",
                softwareGpu = "correctness only; inspect actual device identity before claiming hardware performance",
            };
            await File.WriteAllTextAsync(prefix + "-manifest.json", JsonSerializer.Serialize(manifest, TextChecks.Json));
            await File.WriteAllTextAsync(prefix + "-layout-check.json", JsonSerializer.Serialize(semantics, TextChecks.Json));
            await File.WriteAllLinesAsync(prefix + "-lifecycle.jsonl", assets.Lifecycle.Select(sample => JsonSerializer.Serialize(sample)));
            byte[][] reference = await CheckAsync(device, fixture, assets, slots, method, prefix, "before", crossDepth);
            for (int frame = 0; frame < warmup; frame++)
            {
                TextSlot slot = slots[frame % depth];
                await slot.RetireAsync();
                slot.Frame = await TextFrame.CreateAsync(slot.Renderer, assets.Lists[frame % 2], slot.View);
                slot.Frame.Submit(device, slot.Texture, slot.Timer);
            }

            foreach (TextSlot slot in slots)
            {
                await slot.RetireAsync();
            }

            long start = Stopwatch.GetTimestamp();
            for (int frame = 0; frame < count; frame++)
            {
                long host = Stopwatch.GetTimestamp();
                TextSlot slot = slots[frame % depth];
                TextFrameSample? prior = await slot.RetireAsync();
                if (prior is not null)
                {
                    samples.Add(prior);
                }

                slot.PriorSlotWaitMs = slot.LastWaitMs;
                slot.FrameId = frame;
                slot.State = frame % 2;
                slot.Frame = await TextFrame.CreateAsync(slot.Renderer, assets.Lists[slot.State], slot.View);
                slot.Frame.Submit(device, slot.Texture, slot.Timer);
                slot.HostMs = Stopwatch.GetElapsedTime(host).TotalMilliseconds;
            }

            foreach (TextSlot slot in slots)
            {
                TextFrameSample? last = await slot.RetireAsync();
                if (last is not null)
                {
                    samples.Add(last);
                }
            }

            double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            TextChecks.Require(samples.Count == count && samples.Select(sample => sample.FrameId).Distinct().Count() == count, "Lost frame identities.");
            await CheckAsync(device, fixture, assets, slots, method, prefix, "after", reference);
            await File.WriteAllLinesAsync(prefix + "-frames.jsonl", samples.OrderBy(sample => sample.FrameId).Select(sample => JsonSerializer.Serialize(sample)));
            await File.WriteAllTextAsync(prefix + "-summary.json", JsonSerializer.Serialize(new
            {
                status = "passed",
                smoke,
                count,
                depth,
                elapsedIncludingFinalDrainAndRetirementMs = elapsed,
                headlessFramesPerSecond = count * 1000 / elapsed,
                prepareHostMs = Distribution(samples.Select(sample => sample.PrepareHostMs)),
                recordHostMs = Distribution(samples.Select(sample => sample.RecordHostMs)),
                encodeHostMs = Distribution(samples.Select(sample => sample.EncodeHostMs)),
                hostIncludingSlotWaitMs = Distribution(samples.Select(sample => sample.HostIncludingSlotWaitMs)),
                gpuSpanMs = Distribution(samples.Where(sample => sample.GpuSpanMs.HasValue).Select(sample => sample.GpuSpanMs!.Value)),
            }));
            Console.WriteLine(JsonSerializer.Serialize(new { input.Id, method, depth, smoke, status = "passed", prefix }));
            return reference;
        }
        catch (Exception error)
        {
            await File.WriteAllTextAsync(prefix + "-failure.json", JsonSerializer.Serialize(new { status = "failed", error = error.ToString() }));
            throw;
        }
        finally
        {
            // First confirm every slot, even if query readback/validation failed in another slot.
            // Do not let one timestamp/cleanup error leave a later submission running at teardown.
            foreach (TextSlot slot in slots)
            {
                await slot.ConfirmCompletionAsync();
            }

            foreach (TextSlot slot in slots)
            {
                slot.DiscardCompletedFrame();
                slot.Dispose();
            }
        }
    }

    private static async Task<byte[][]> CheckAsync(VulkanDevice device, TextFixture fixture, TextAssets assets, List<TextSlot> slots, string method, string prefix, string phase, byte[][]? reference)
    {
        byte[][] expected = reference ?? new byte[2][];
        foreach (int state in new[] { 0, 1, 0 })
        {
            foreach (TextSlot slot in slots)
            {
                slot.Frame = await TextFrame.CreateAsync(slot.Renderer, assets.Lists[state], slot.View);
                slot.Frame.Submit(device, slot.Texture, slot.Timer);
            }

            foreach (TextSlot slot in slots)
            {
                await slot.RetireAsync();
                byte[] pixels = await PixelReadback.ReadAsync(device, slot.Texture);
                object quality = TextPixels.Verify(fixture.Input, fixture.Anchor, pixels);
                expected[state] ??= pixels;
                TextChecks.Require(expected[state].AsSpan().SequenceEqual(pixels), "Same-method text pixel/slot/reuse/depth determinism failed.");
                if (slot.Index == 0 && phase == "before")
                {
                    await TextPixels.SaveAsync($"{prefix}-{phase}-state{state}", pixels);
                }

                var checkRow = new
                {
                    phase,
                    state,
                    slot = slot.Index,
                    quality,
                    sha256 = Convert.ToHexString(SHA256.HashData(pixels)),
                    status = "passed",
                };
                await File.AppendAllTextAsync(prefix + "-checks.jsonl", JsonSerializer.Serialize(checkRow) + Environment.NewLine);
            }

            if (method == "vector")
            {
                TextSlot slot = slots[0];
                DrawList2D logicalReference = assets.VectorReference(fixture.Anchor, state);
                slot.Frame = await TextFrame.CreateAsync(slot.Renderer, logicalReference, slot.View);
                slot.Frame.Submit(device, slot.Texture, slot.Timer);
                await slot.RetireAsync();
                byte[] pixels = await PixelReadback.ReadAsync(device, slot.Texture);
                TextChecks.Require(expected[state].AsSpan().SequenceEqual(pixels), "DrawText differs from independent logical FillPath/paint adapter reference.");
            }
        }

        bool changed = !expected[0].AsSpan().SequenceEqual(expected[1]);
        TextChecks.Require(changed == fixture.Input.Changes, "Expected visible text/width change was absent, or static text changed.");
        var transitionRow = new
        {
            phase,
            expectedVisibleChange = fixture.Input.Changes,
            actualVisibleChange = changed,
            status = "passed",
        };
        await File.AppendAllTextAsync(prefix + "-checks.jsonl", JsonSerializer.Serialize(transitionRow) + Environment.NewLine);
        return expected;
    }

    private static object? Distribution(IEnumerable<double> input)
    {
        double[] sorted = input.Order().ToArray();
        return sorted.Length == 0 ? null : new
        {
            median = sorted.Length % 2 == 0 ? (sorted[(sorted.Length / 2) - 1] + sorted[sorted.Length / 2]) / 2 : sorted[sorted.Length / 2],
            p95 = sorted[(int)Math.Ceiling(sorted.Length * 0.95) - 1],
            maximum = sorted[^1],
            samples = sorted.Length,
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
