using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Lumyte.Build;

internal static partial class ComponentScreening
{
    public const string Version = "component-screening-v5";
    public const string SharedHarness = "benchmarks/Core/Shared";
    public const string Harness = "benchmarks/Core/Lumyte.Signals.Benchmark";
    public static readonly ScreeningCase[] Catalog =
    [
        new("Lumyte.Signals.Benchmark.Values.ScalarBenchmarks.ReadValue", 1),
        new("Lumyte.Signals.Benchmark.Values.ScalarBenchmarks.ReadPeek", 1),
        new("Lumyte.Signals.Benchmark.Values.ScalarBenchmarks.WriteSame", 1),
        new("Lumyte.Signals.Benchmark.Values.ScalarBenchmarks.WriteUnobserved", 2),
        new("Lumyte.Signals.Benchmark.Async.AsyncBenchmarks.CreateCompleted", 20),
        new("Lumyte.Signals.Benchmark.Async.AsyncBenchmarks.ReloadCompleted", 20),
        new("Lumyte.Signals.Benchmark.Async.AsyncBenchmarks.ReloadCompletedObserved", 30),
        new("Lumyte.Signals.Benchmark.Async.AsyncBenchmarks.SupersedeAndCancelPending", 100),
        new("Lumyte.Signals.Benchmark.Computed.GraphBenchmarks.DiamondUpdate", 20),
        new("Lumyte.Signals.Benchmark.Computed.GraphBenchmarks.SwitchBranch", 20),
        new("Lumyte.Signals.Benchmark.Computed.GraphBenchmarks.CaptureEightReads", 20),
        new("Lumyte.Signals.Benchmark.Computed.GraphBenchmarks.CheckReadSet", 5),
    ];

    private static readonly string[] _signalsPrefixes = ["src/Core/Lumyte.Signals/", "benchmarks/Lumyte.Signals.Benchmarks/", Harness + "/"];
    private static readonly string[] _infrastructurePrefixes = ["build/", "tools/", ".github/", "benchmarks/Lumyte.Component.Screening/", SharedHarness + "/"];
    private static readonly string[] _hostFields = ["BenchmarkDotNetVersion", "OsVersion", "ProcessorName", "Architecture", "RuntimeVersion", "Configuration", "DotNetCliVersion"];
    private static readonly string[] _jobFields = ["SignalsScreening-v2", "OutlierMode=DontRemove", "IterationCount=8", "IterationTime=150ms", "LaunchCount=1", "WarmupCount=3"];

    public static (ScreeningCase[] Cases, string[] Uncovered, string[] Omissions) Select(IEnumerable<string> paths, bool all = false)
    {
        string[] relevant = paths.Where(p => !p.StartsWith("docs/", StringComparison.Ordinal) && !p.EndsWith(".md", StringComparison.Ordinal)).ToArray();
        if (relevant.Length == 0 && !all)
        {
            return ([], [], []);
        }

        bool Known(string p) => _signalsPrefixes.Any(prefix => p.StartsWith(prefix, StringComparison.Ordinal));
        bool Common(string p) => _infrastructurePrefixes.Any(prefix => p.StartsWith(prefix, StringComparison.Ordinal)) || p.StartsWith("Directory.", StringComparison.Ordinal)
            || new[] { "global.json", "NuGet.config", "mise.toml", "mise.lock", ".editorconfig" }.Contains(p)
            || new[] { ".props", ".targets", ".slnx" }.Any(suffix => p.EndsWith(suffix, StringComparison.Ordinal));
        return (all || relevant.Any(p => Known(p) || Common(p)) ? Catalog.ToArray() : [Catalog[0]],
            relevant.Where(p => !Known(p) && !Common(p)).ToArray(),
            ["Signals computed/fanout/batch/scheduler/shared-DAG/reuse/validation workloads are not covered.",
             "Mathematics, Execution and Graphics are not covered; sentinel timing does not measure them."]);
    }

    public static double Finite(JsonNode? value, string name, bool positive = false)
    {
        if (value is not JsonValue scalar || !scalar.TryGetValue<double>(out double number) || !double.IsFinite(number) || number < 0 || (positive && number == 0))
        {
            throw new ScreeningFailure("invalid_data", "Missing/invalid " + name);
        }

        return number;
    }

    public static JsonObject ReadBdn(string directory, ScreeningCase[] cases)
    {
        var rows = new JsonObject();
        string[] files = Directory.Exists(directory) ? Directory.GetFiles(directory, "*-full.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray() : [];
        if (files.Length == 0)
        {
            throw new ScreeningFailure("missing_data", "No full BDN JSON under " + directory);
        }

        JsonObject? hostIdentity = null;
        foreach (string file in files)
        {
            JsonNode document = JsonNode.Parse(File.ReadAllText(file))!;
            JsonNode? host = document["HostEnvironmentInfo"];
            if (host is null || _hostFields.Any(key => string.IsNullOrEmpty(host[key]?.ToString())))
            {
                throw new ScreeningFailure("missing_data", "Missing BDN host identity");
            }

            var current = new JsonObject();
            foreach (string field in _hostFields)
            {
                current[field] = host[field]!.DeepClone();
            }

            if ((hostIdentity is not null && !JsonNode.DeepEquals(hostIdentity, current)) || host["BenchmarkDotNetVersion"]!.ToString() != "0.15.8" || host["Configuration"]!.ToString() != "RELEASE")
            {
                throw new ScreeningFailure("incomparable", "Unsupported or differing BDN host identities/configuration");
            }

            hostIdentity = current;
            foreach (JsonNode? row in document["Benchmarks"]?.AsArray() ?? [])
            {
                string? id = row?["FullName"]?.GetValue<string>();
                if (id is null || !cases.Any(c => c.Id == id) || rows.ContainsKey(id))
                {
                    throw new ScreeningFailure("incomparable", "Unexpected/duplicate benchmark identity: " + id);
                }

                if (row!["Parameters"]?.ToString() != string.Empty || _jobFields.Any(part => !Regex.IsMatch(row["DisplayInfo"]?.ToString() ?? string.Empty, "(?<![A-Za-z0-9])" + Regex.Escape(part) + "(?![A-Za-z0-9.])", RegexOptions.CultureInvariant)))
                {
                    throw new ScreeningFailure("incomparable", id + ": unexpected parameters or job configuration");
                }

                JsonNode? stats = row["Statistics"];
                double median = Finite(stats?["Median"], "median ns", true);
                double mean = Finite(stats?["Mean"], "mean ns", true);
                double deviation = Finite(stats?["StandardDeviation"], "standard deviation ns");
                double allocation = Finite(row["Memory"]?["BytesAllocatedPerOperation"], "allocated bytes");
                JsonNode?[] actual = (row["Measurements"]?.AsArray() ?? []).Where(m => m?["IterationStage"]?.ToString() == "Result" && m["IterationMode"]?.ToString() == "Workload").ToArray();
                if (actual.Length != 8 || Finite(stats?["N"], "sample count") != 8)
                {
                    throw new ScreeningFailure("invalid_data", id + ": expected 8 retained workload results");
                }

                foreach (JsonNode? sample in actual)
                {
                    Finite(sample?["Nanoseconds"], "measurement nanoseconds", true);
                    Finite(sample?["Operations"], "measurement operations", true);
                }

                rows[id] = new JsonObject
                {
                    ["median_ns"] = median,
                    ["mean_ns"] = mean,
                    ["stddev_ns"] = deviation,
                    ["allocated_bytes"] = allocation,
                    ["samples"] = actual.Length,
                    ["raw_file"] = file,
                    ["host_identity"] = current.DeepClone(),
                    ["hardware_intrinsics"] = row["HardwareIntrinsics"]?.DeepClone(),
                };
            }
        }

        if (rows.Count != cases.Length)
        {
            throw new ScreeningFailure("missing_data", "Missing benchmark identities");
        }

        return rows;
    }

    public static JsonArray Compare(JsonObject baseline, JsonObject head, ScreeningCase[] cases)
    {
        if (baseline.Count != cases.Length || head.Count != cases.Length || cases.Any(c => !baseline.ContainsKey(c.Id) || !head.ContainsKey(c.Id)))
        {
            throw new ScreeningFailure("incomparable", "Base/head case identities differ from frozen plan");
        }

        var comparisons = new JsonArray();
        foreach (ScreeningCase item in cases)
        {
            JsonNode before = baseline[item.Id]!;
            JsonNode after = head[item.Id]!;
            if (!JsonNode.DeepEquals(before["host_identity"], after["host_identity"]) || !JsonNode.DeepEquals(before["hardware_intrinsics"], after["hardware_intrinsics"]))
            {
                throw new ScreeningFailure("incomparable", item.Id + ": runtime/CPU/SDK identities differ");
            }

            double initial = Finite(before["median_ns"], "base median", true);
            double delta = Finite(after["median_ns"], "head median", true) - initial;
            double percent = 100 * delta / initial;
            double alloc = Finite(before["allocated_bytes"], "base allocation");
            double allocDelta = Finite(after["allocated_bytes"], "head allocation") - alloc;
            comparisons.Add(new JsonObject
            {
                ["id"] = item.Id,
                ["base"] = before.DeepClone(),
                ["head"] = after.DeepClone(),
                ["delta_ns"] = delta,
                ["delta_percent"] = percent,
                ["min_delta_ns"] = item.MinDeltaNs,
                ["latency_candidate"] = percent >= 10 - 1e-9 && delta >= item.MinDeltaNs,
                ["allocation_increase"] = allocDelta > 0,
                ["allocation_delta_bytes"] = allocDelta,
                ["allocation_delta_percent"] = alloc == 0 ? null : 100 * allocDelta / alloc,
            });
        }

        return comparisons;
    }
}
