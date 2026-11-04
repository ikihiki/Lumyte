using System.Diagnostics;
using System.Text.Json;
using Lumyte.Graphics.Incremental.Benchmarks;
using Lumyte.Graphics.Vulkan;

namespace Lumyte.Graphics.UiScenarios;

internal static class Program
{
    private static void Emit(object o) => Console.WriteLine(JsonSerializer.Serialize(o));

    private static string? Option(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static int Number(string[] args, string name, int fallback) => Option(args, name) is string s ? int.Parse(s, System.Globalization.CultureInfo.InvariantCulture) : fallback;

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static int Main(string[] args) => RunAsync(args).GetAwaiter().GetResult();

    private static async Task<int> RunAsync(string[] args)
    {
        try
        {
            string mode = CliOptions.Validate(args);
            if (mode == "--list")
            {
                foreach (UiProfile p in UiProfile.Standard)
                {
                    Emit(new
                    {
                        p.Key,
                    });
                }

                return 0;
            }

            if (mode == "--unit")
            {
                Unit();
                return 0;
            }

            UiProfile[] selected = UiProfile.Standard.Where(p => Option(args, "--case") is not string id || p.Id == id)
                .Where(p => Option(args, "--variant") is not string v || p.Variant == v).ToArray();
            Require(selected.Length > 0, "No selected UI profile.");
            if (mode == "--smoke")
            {
                Unit();
                foreach (UiProfile p in selected)
                {
                    Smoke(p);
                }

                return 0;
            }

            if (mode != "--gpu")
            {
                throw new ArgumentException("Use --list, --unit, --smoke or --gpu; --case U01..U07, --variant NAME, --depth 1|3, --warmup N, --frames N.");
            }

            int depth = Number(args, "--depth", 1);
            int warmup = Number(args, "--warmup", 200);
            int frames = Number(args, "--frames", 2000);
            Require(depth is 1 or 3 && warmup >= 0 && warmup <= 2000 && frames > 0 && frames <= 100000, "Depth must be 1 or 3; warmup must be 0..2000 and frames 1..100000.");
            long deviceStarted = Stopwatch.GetTimestamp();
            var device = VulkanDevice.Create();
            double deviceInitializationMs = Stopwatch.GetElapsedTime(deviceStarted).TotalMilliseconds;
            try
            {
                // Validate the actual physical device before issuing any workload.
                // This identity-only timer owns no submitted query work.
                using var identity = new HardwareGpuTimer(device);
                Emit(new
                {
                    scope = "manifest",
                    schemaVersion = 1,
                    scenarioVersion = "ui-v2",
                    actualDeviceBackend = device.GetType().FullName,
                    device.Caps,
                    device.Status,
                    physicalDeviceIndex = 0,
                    actualDeviceAndDriver = identity.DeviceDescription,
                    timestampSupported = identity.Supported,
                    requiredHardware = "Radeon 8060S; vendor=0x1002; device=0x1586; non-CPU",
                    source = Environment.GetEnvironmentVariable("DRAWING_BASELINE"),
                    deviceInitializationMs,
                    coldScope = "First local font/mount/layout and first actual preflight render stages are recorded separately; use a fresh process per cold observation; driver/native disk caches are not asserted cold",
                    provenance = Lumyte.Graphics.Gpu.Benchmarks.Provenance.Capture(),
                    runtime = Environment.Version.ToString(),
                    os = Environment.OSVersion.ToString(),
                    icd = Environment.GetEnvironmentVariable("VK_DRIVER_FILES"),
                    format = "Rgba8Srgb",
                    target = "profile-selected physical pixels",
                    depth,
                    gpuTimestamp = "slot-owned Vulkan TOP_OF_PIPE -> BOTTOM_OF_PIPE; initial target barrier excluded; clear/draw/inter-layer barriers included; uploads inside Prepare excluded",
                    gpuSpanLocation = "completion rows joined by profile key, frame ID and input ID; unsupported null, never fence time",
                    presentation = false,
                    scopeContract = "real UiSurface/Signal/native Parley input+layout+record through GPU completion; fresh mounts and pixel checks outside timing",
                    gaps = new[]
                    {
                        "U02 isolated group-opacity variant not implemented; transform-only and layout are real",
                        "U03 actual host no-draw idle/resume not measured",
                        "U07 3D preview not implemented",
                        "D07 full text rendering suite not claimed",
                        "U05 TextBox wrapping unavailable on this selected product control: TextBoxElement shapes with positive infinity; paragraph wrap not substituted",
                    },
                });
                foreach (UiProfile p in selected)
                {
                    await UiGpuRunner.RunAsync(device, p, warmup, frames, depth, Emit);
                }
            }
            finally
            {
                if (UiGpuRunner.Quarantine.Count == 0)
                {
                    device.Dispose();
                }
                else
                {
                    UiGpuRunner.Quarantine.Add(device);
                }
            }

            return 0;
        }
        catch (Exception e)
        {
            Emit(new
            {
                status = "failed",
                error = e.ToString(),
                performanceValid = false,
            });
            return 1;
        }
    }

    private static void Unit()
    {
        Require(UiProfile.Standard.Select(p => p.Key).Distinct().Count() == UiProfile.Standard.Length, "Duplicate profile key.");
        Require(UiProfile.Standard.Select(p => p.Id).Distinct().Count() == 7, "Missing UI scenario ID.");
        foreach (UiProfile p in UiProfile.Standard)
        {
            for (int f = 0; f < 240; f++)
            {
                var a = UiInput.At(p, f);
                var b = UiInput.At(p, f);
                Require(a == b && a.InputId == b.InputId, "Nondeterministic trace.");
                Require(a.FirstRow >= 0 && a.FirstRow + p.VisibleRows <= 10000, "Visible range invalid.");
                Require(a.TargetWidth > 0 && a.TargetHeight > 0, "Target invalid.");
            }

            if (p.Id == "U01")
            {
                int events = Enumerable.Range(1, p.Hz).Count(f => UiInput.At(p, f).HudValue != UiInput.At(p, f - 1).HudValue);
                Require(events == 10, "HUD must update exactly ten times per logical second.");
            }

            if (p.Id == "U04" && p.Variant == "fractional")
            {
                Require(UiInput.At(p, 15).FirstRow == 0 && UiInput.At(p, 15).Scroll > 0, "Fractional trace crossed a row.");
            }

            if (p.Id == "U06" && p.Variant == "dpi-once")
            {
                Require(UiInput.At(p, 29).Dpr == 1 && UiInput.At(p, 30).Dpr == 2 && UiInput.At(p, 60).Dpr == 2, "DPI transition invalid.");
            }
        }

        Emit(new
        {
            check = "ui-trace-unit",
            profiles = UiProfile.Standard.Length,
            framesPerProfile = 240,
            status = "passed",
            nativeTextRequired = false,
        });
    }

    private static void Smoke(UiProfile p)
    {
        using var fixture = new UiScenario(p);
        fixture.Layout();
        IReadOnlyList<Lumyte.Graphics.Drawing2D.DrawList2D> initial = fixture.Record();
        Emit(fixture.Verify());
        long initialRebuilds = fixture.Rebuilds;
        for (int frame = 1; frame <= 120; frame++)
        {
            fixture.Update(frame);
            fixture.Layout();
            IReadOnlyList<Lumyte.Graphics.Drawing2D.DrawList2D> layers = fixture.Record();
            if (p.Id == "U03")
            {
                Require(ReferenceEquals(initial[1], layers[1]), "Static UI DrawList was not reused.");
            }

            if (frame is 6 or 12 or 30 or 60 or 120)
            {
                Emit(fixture.Verify());
            }

            Require(layers.All(l => l.Count > 0), "Empty recorded UI layer.");
        }

        if (p.Id == "U01")
        {
            Require(fixture.SignalSets == 2 * (120 / (p.Hz / 10)), "Wrong Signal event count.");
        }

        if (p.Id == "U02" && p.Variant == "transform")
        {
            Require(fixture.Rebuilds == initialRebuilds, "Visual animation unexpectedly rebuilt layout declarations.");
        }

        if (p.Id == "U07")
        {
            Require(fixture.Rebuilds == 2, "Preview should rebuild surrounding UI only at input events.");
        }

        Require(fixture.Reference().All(l => l.Count > 0), "Empty independent mount reference.");
        Emit(new
        {
            check = "ui-native-smoke",
            p.Key,
            fixture.FontHash,
            status = "passed",
            counters = fixture.Counters(),
            gpuPixelCheck = "not run; use --gpu",
        });
    }
}
