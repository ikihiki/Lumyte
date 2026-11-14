using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lumyte.Build;

// Data policy for BenchmarkCoreEvaluate. No clocks or benchmark execution lives here.
internal static class CoreBenchmarkEvaluation
{
    internal static void Validate(string configuration, string filter, double timeout)
    {
        if (configuration != "Release")
        {
            throw new ArgumentException("Core evaluation requires Release configuration.");
        }

        if (filter != "*")
        {
            throw new ArgumentException("BenchmarkCoreEvaluate requires --benchmark-filter '*'; use BenchmarkCore for method globs.");
        }

        if (!double.IsFinite(timeout) || timeout <= 0 || timeout > 900)
        {
            throw new ArgumentException("Each preparation command and timed partition deadline must be positive and at most 900 seconds.");
        }
    }

    internal static string RequiredString(JsonNode? node, string name)
        => node?[name] is JsonValue value && value.TryGetValue<string>(out string? text) && !string.IsNullOrWhiteSpace(text)
            ? text : throw new ArgumentException("Missing or invalid " + name);

    internal static bool Has(JsonNode item, string field, string value)
        => item[field]!.AsArray().Any(entry => entry!.GetValue<string>() == value);

    internal static string? Exclusion(JsonNode item, string? mode, string? category)
        => !Has(item, "Categories", "Standalone") || Has(item, "Categories", "DependencyInjection") || Has(item, "Categories", "Integration") ? "excluded-scope"
        : !string.IsNullOrWhiteSpace(category) && !Has(item, "Categories", category) ? "excluded-category"
        : mode is not null && !Has(item, "Modes", mode) ? "excluded-mode" : null;

    internal static string Partition(JsonNode item) => RequiredString(item, "Type") + "." + RequiredString(item, "Method");

    internal static string CaseKey(JsonNode item) => RequiredString(item, "ResultKey");

    internal static double? BudgetShare(JsonNode benchmark, double medianNanoseconds, bool enabled)
        => enabled && Has(benchmark, "Categories", "SteadyState") ? medianNanoseconds / (1_000_000_000d / 120) * 100 : null;

    internal static string EnvironmentIdentity(JsonNode? environment)
        => string.Join("|", new[] { "BenchmarkDotNetVersion", "OsVersion", "ProcessorName", "RuntimeVersion", "Architecture", "Configuration", "DotNetCliVersion" }
            .Select(field => field + "=" + RequiredString(environment, field)));

    internal static JsonArray ParseReports(IEnumerable<string> files, string? mode = null)
    {
        var rows = new JsonArray();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (string file in files.Order(StringComparer.Ordinal))
        {
            JsonObject report = JsonNode.Parse(File.ReadAllText(file)) as JsonObject ?? throw new ArgumentException("Invalid full BDN JSON: " + file);
            if (report["Benchmarks"] is not JsonArray benchmarks || benchmarks.Count == 0)
            {
                throw new ArgumentException("Full BDN JSON has no benchmarks: " + file);
            }

            foreach (JsonNode? item in benchmarks)
            {
                string fullName = RequiredString(item, "FullName");
                string parameters = item?["Parameters"]?.GetValue<string>() ?? throw new ArgumentException("Missing BDN Parameters.");
                string key = fullName + "|" + parameters;
                if (!keys.Add(key))
                {
                    throw new ArgumentException("Duplicate observed benchmark case: " + key);
                }

                JsonNode statistics = item?["Statistics"] ?? throw new ArgumentException("Failed benchmark has no Statistics: " + key);
                double[] values = statistics["OriginalValues"]?.AsArray().Select(value => Nonnegative(value, "iteration ns/op")).ToArray()
                    ?? throw new ArgumentException("Missing OriginalValues: " + key);
                if (values.Length == 0 || statistics["N"]?.GetValue<int>() != values.Length)
                {
                    throw new ArgumentException("Empty or inconsistent Statistics: " + key);
                }

                if (item?["Measurements"] is not JsonArray measurements || !measurements.Any(measurement =>
                    measurement?["IterationMode"]?.GetValue<string>() == "Workload" && measurement?["IterationStage"]?.GetValue<string>() == "Result"))
                {
                    throw new ArgumentException("Missing full result measurements: " + key);
                }

                if (mode is not null)
                {
                    BenchmarkCatalog.NormalizeMode(mode);
                    EnvironmentIdentity(report["HostEnvironmentInfo"]);
                    if (!RequiredString(item, "DisplayInfo").Contains(": Core-" + mode + "(", StringComparison.Ordinal))
                    {
                        throw new ArgumentException("Unexpected BDN job for " + key);
                    }

                    JsonNode?[] final = measurements.Where(measurement => measurement?["IterationMode"]?.GetValue<string>() == "Workload"
                        && measurement?["IterationStage"]?.GetValue<string>() == "Result").ToArray();
                    if (final.Length != values.Length || (mode is "screen" or "diagnose" && values.Length != 8)
                        || (mode == "compare" && final.Select(measurement => measurement!["LaunchIndex"]!.GetValue<int>()).Distinct().Count() != 3))
                    {
                        throw new ArgumentException("Incomplete result iteration/launch count for " + mode + ": " + key);
                    }

                    foreach (JsonNode? measurement in final)
                    {
                        if (Nonnegative(measurement?["Operations"], "Operations") <= 0)
                        {
                            throw new ArgumentException("Result measurements require positive operations.");
                        }

                        Nonnegative(measurement?["Nanoseconds"], "Nanoseconds");
                    }
                }

                double bytes = Nonnegative(item?["Memory"]?["BytesAllocatedPerOperation"], "BytesAllocatedPerOperation");
                Array.Sort(values);
                double mean = values.Average();
                double deviation = values.Length < 2 ? 0 : Math.Sqrt(values.Sum(value => Math.Pow(value - mean, 2)) / (values.Length - 1));
                rows.Add(new JsonObject
                {
                    ["key"] = key,
                    ["median_ns"] = Percentile(values, 0.5),
                    ["p95_iteration_ns"] = Percentile(values, 0.95),
                    ["standard_deviation_ns"] = deviation,
                    ["iqr_ns"] = Percentile(values, 0.75) - Percentile(values, 0.25),
                    ["bytes_per_operation"] = bytes,
                    ["iteration_count"] = values.Length,
                    ["raw_report"] = Path.GetFullPath(file),
                    ["host_environment"] = report["HostEnvironmentInfo"]?.DeepClone(),
                });
            }
        }

        return rows;
    }

    internal static double Percentile(double[] sorted, double probability)
    {
        if (sorted.Length == 0 || !double.IsFinite(probability) || probability < 0 || probability > 1)
        {
            throw new ArgumentException("Percentiles require nonempty sorted observations and probability in [0,1].");
        }

        double position = (sorted.Length - 1) * probability;
        int lower = (int)position;
        int upper = Math.Min(lower + 1, sorted.Length - 1);
        return sorted[lower] + ((sorted[upper] - sorted[lower]) * (position - lower));
    }

    internal static JsonObject Coverage(IEnumerable<string> expected, IEnumerable<string> observed, bool filtered)
    {
        string[] wanted = expected.ToArray();
        string[] actual = observed.ToArray();
        if (wanted.Distinct(StringComparer.Ordinal).Count() != wanted.Length || actual.Distinct(StringComparer.Ordinal).Count() != actual.Length)
        {
            throw new ArgumentException("Duplicate expected or observed expanded case identity.");
        }

        string[] missing = wanted.Except(actual, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        string[] extra = actual.Except(wanted, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        bool complete = wanted.Length > 0 && missing.Length == 0 && extra.Length == 0;
        return new JsonObject
        {
            ["expected_expanded_cases"] = wanted.Length,
            ["observed_expanded_cases"] = actual.Length,
            ["observed_definition"] = "Validated successful full-JSON cases; failed or unparsed raw exports are not successful observations.",
            ["missing"] = JsonSerializer.SerializeToNode(missing),
            ["unexpected"] = JsonSerializer.SerializeToNode(extra),
            ["selected_coverage_complete"] = complete,
            ["full_category_complete"] = complete && !filtered,
            ["scope"] = filtered ? "filtered-subset" : "all-standalone-mode-eligible-core",
        };
    }

    internal static void RequireComplete(JsonObject coverage)
    {
        if (coverage["selected_coverage_complete"]?.GetValue<bool>() != true)
        {
            throw new ArgumentException("Incomplete expanded benchmark coverage: " + coverage.ToJsonString());
        }
    }

    internal static string Markdown(JsonObject manifest)
    {
        var text = new StringBuilder("# Standalone Core evaluation\n\n");
        text.AppendLine("Status: " + manifest["status"] + ". Scope: " + manifest["coverage"]?["scope"] + ".");
        text.AppendLine("\nBDN iteration estimates (ns/op), not individual frame latency p95. No summed frame time or real-frame percentile is implied.");
        text.AppendLine("\nCoverage: " + manifest["coverage"]?.ToJsonString());
        text.AppendLine("\n| Case | Median ns | p95 iteration ns | SD ns | IQR ns | Bytes/op | 8.333 ms share % (SteadyState only) |");
        text.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
        foreach (JsonNode? row in manifest["results"]!.AsArray())
        {
            string[] fields = ["key", "median_ns", "p95_iteration_ns", "standard_deviation_ns", "iqr_ns", "bytes_per_operation", "budget_share_percent"];
            text.AppendLine("| " + string.Join(" | ", fields.Select(field => (row?[field]?.ToString() ?? "—").Replace("|", "\\|", StringComparison.Ordinal))) + " |");
        }

        if (manifest["error"] is not null)
        {
            text.AppendLine("\nError: " + manifest["error"]);
        }

        return text.ToString();
    }

    private static double Nonnegative(JsonNode? node, string label)
        => node is JsonValue value && value.TryGetValue<double>(out double number) && double.IsFinite(number) && number >= 0
            ? number : throw new ArgumentException("Missing, nonfinite or negative " + label);
}
