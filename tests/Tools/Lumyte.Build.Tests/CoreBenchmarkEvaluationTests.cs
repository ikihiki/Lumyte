using System.Text.Json.Nodes;
using Xunit;

namespace Lumyte.Build.Tests;

/// <summary>Deterministic Core evaluation policy, BDN report and coverage contracts.</summary>
public sealed class CoreBenchmarkEvaluationTests
{
    /// <summary>Selection combines only an optional purpose and mode; all compiled cases are eligible for listing.</summary>
    [Fact]
    public void ExclusionUsesPurposeAndModeOnly()
    {
        JsonObject benchmark = Case();
        Assert.Null(CoreBenchmarkEvaluation.Exclusion(benchmark, "screen", "Screening"));
        Assert.Equal("excluded-mode", CoreBenchmarkEvaluation.Exclusion(benchmark, "compare", "Screening"));
        Assert.Equal("excluded-category", CoreBenchmarkEvaluation.Exclusion(benchmark, "screen", "Diagnostic"));
        Assert.Null(CoreBenchmarkEvaluation.Exclusion(benchmark, null, null));
        benchmark["Categories"] = new JsonArray("Diagnostic");
        benchmark["Modes"] = new JsonArray("diagnose");
        Assert.Null(CoreBenchmarkEvaluation.Exclusion(benchmark, "diagnose", "Diagnostic"));
        Assert.Equal("excluded-mode", CoreBenchmarkEvaluation.Exclusion(benchmark, "screen", null));
        Assert.Throws<ArgumentException>(() => CoreBenchmarkEvaluation.Exclusion(benchmark, null, "Geometry"));
    }

    /// <summary>Missing, extra, duplicate and empty observations fail exact expanded coverage.</summary>
    [Fact]
    public void CoverageIsExactAndNeverVacuous()
    {
        Assert.Throws<ArgumentException>(() => CoreBenchmarkEvaluation.RequireComplete(CoreBenchmarkEvaluation.Coverage([], [], false)));
        JsonObject incomplete = CoreBenchmarkEvaluation.Coverage(["A", "B"], ["A", "C"], false);
        Assert.Equal("B", incomplete["missing"]![0]!.GetValue<string>());
        Assert.Equal("C", incomplete["unexpected"]![0]!.GetValue<string>());
        Assert.Throws<ArgumentException>(() => CoreBenchmarkEvaluation.RequireComplete(incomplete));
        Assert.Throws<ArgumentException>(() => CoreBenchmarkEvaluation.Coverage(["A", "A"], ["A"], false));
        Assert.Throws<ArgumentException>(() => CoreBenchmarkEvaluation.Coverage(["A"], ["A", "A"], false));
        JsonObject subset = CoreBenchmarkEvaluation.Coverage(["A"], ["A"], true);
        CoreBenchmarkEvaluation.RequireComplete(subset);
        Assert.False(subset["full_category_complete"]!.GetValue<bool>());
        Assert.True(CoreBenchmarkEvaluation.Coverage(["A"], ["A"], false)["full_category_complete"]!.GetValue<bool>());
    }

    /// <summary>Policy accepts native namespace/class/method globs and rejects empty filters or invalid deadlines.</summary>
    [Fact]
    public void EvaluationPolicyFailsClosed()
    {
        CoreBenchmarkEvaluation.Validate("Release", "*", 900);
        Assert.Throws<ArgumentException>(() => CoreBenchmarkEvaluation.Validate("Debug", "*", 900));
        foreach (string filter in new[] { "*Method*", "Example.Geometry.*", "*Fixture.*", "Example.Fixture.Measure" })
        {
            CoreBenchmarkEvaluation.Validate("Release", filter, 900);
        }

        foreach (string filter in new[] { string.Empty, " " })
        {
            Assert.Throws<ArgumentException>(() => CoreBenchmarkEvaluation.Validate("Release", filter, 900));
        }

        foreach (string purpose in new[] { "Screening", "Comparison", "Diagnostic" })
        {
            CoreBenchmarkEvaluation.Validate("Release", "*", 900, purpose);
        }

        Assert.Throws<ArgumentException>(() => CoreBenchmarkEvaluation.Validate("Release", "*", 900, "Geometry"));
        Assert.Throws<ArgumentException>(() => CoreBenchmarkEvaluation.Validate("Release", "*", 900, string.Empty));
        foreach (double limit in new[] { 0, -1, 901, double.NaN, double.PositiveInfinity })
        {
            Assert.Throws<ArgumentException>(() => CoreBenchmarkEvaluation.Validate("Release", "*", limit));
        }
    }

    /// <summary>Compiled BDN ResultKey is authoritative; no source-value display approximations.</summary>
    [Fact]
    public void ParameterKeysUseCompiledBdnIdentity()
    {
        JsonObject benchmark = Case();
        benchmark["ResultKey"] = "Example.Fixture.Measure|Count=32&Ratio=0.375&Enabled=True&Name=Target";
        Assert.Equal("Example.Fixture.Measure|Count=32&Ratio=0.375&Enabled=True&Name=Target", CoreBenchmarkEvaluation.CaseKey(benchmark));
        benchmark.Remove("ResultKey");
        Assert.Throws<ArgumentException>(() => CoreBenchmarkEvaluation.CaseKey(benchmark));
    }

    /// <summary>Full JSON is parsed with iteration percentiles, dispersion and allocation values including zero.</summary>
    [Fact]
    public void ParsesFullReportsWithoutConfusingWarmupOrFramePercentiles()
    {
        using var directory = new ScreeningTemporaryDirectory();
        string file = WriteReport(directory.Path, Report());
        JsonNode row = Assert.Single(CoreBenchmarkEvaluation.ParseReports([file]))!;
        Assert.Equal("Example.Fixture.Measure|", row["key"]!.GetValue<string>());
        Assert.Equal(2.5, row["median_ns"]!.GetValue<double>());
        Assert.Equal(3.85, row["p95_iteration_ns"]!.GetValue<double>(), 10);
        Assert.Equal(1.5, row["iqr_ns"]!.GetValue<double>());
        Assert.Equal(Math.Sqrt(5.0 / 3), row["standard_deviation_ns"]!.GetValue<double>(), 10);
        Assert.Equal(0, row["bytes_per_operation"]!.GetValue<double>());
        Assert.Throws<ArgumentException>(() => CoreBenchmarkEvaluation.ParseReports([file, file]));
        JsonObject report = Report();
        report["Benchmarks"]![0]!["Memory"]!["BytesAllocatedPerOperation"] = 96;
        WriteReport(directory.Path, report);
        Assert.Equal(96, Assert.Single(CoreBenchmarkEvaluation.ParseReports([file]))!["bytes_per_operation"]!.GetValue<double>());
    }

    /// <summary>Missing/failed/empty statistics and missing allocation data never become zero-cost success.</summary>
    [Fact]
    public void RejectsFailedAndIncompleteReports()
    {
        using var directory = new ScreeningTemporaryDirectory();
        foreach (string field in new[] { "Statistics", "Memory", "Measurements" })
        {
            JsonObject report = Report();
            report["Benchmarks"]![0]!.AsObject().Remove(field);
            string file = WriteReport(directory.Path, report);
            Assert.Throws<ArgumentException>(() => CoreBenchmarkEvaluation.ParseReports([file]));
        }

        JsonObject empty = Report();
        empty["Benchmarks"]![0]!["Statistics"]!["OriginalValues"] = new JsonArray();
        Assert.Throws<ArgumentException>(() => CoreBenchmarkEvaluation.ParseReports([WriteReport(directory.Path, empty)]));
        JsonObject negative = Report();
        negative["Benchmarks"]![0]!["Memory"]!["BytesAllocatedPerOperation"] = -1;
        Assert.Throws<ArgumentException>(() => CoreBenchmarkEvaluation.ParseReports([WriteReport(directory.Path, negative)]));
        Assert.Empty(CoreBenchmarkEvaluation.ParseReports([]));
        Assert.Throws<ArgumentException>(() => CoreBenchmarkEvaluation.Percentile([], 0.95));
    }

    /// <summary>Timed screen/diagnose reports require the declared job and exactly eight final results.</summary>
    [Fact]
    public void RejectsUndersampledAndWrongModeResults()
    {
        using var directory = new ScreeningTemporaryDirectory();
        JsonObject report = Report();
        report["HostEnvironmentInfo"] = JsonNode.Parse("""{"BenchmarkDotNetVersion":"0.15.8","OsVersion":"test OS","ProcessorName":"test CPU","RuntimeVersion":".NET 10","Architecture":"X64","Configuration":"RELEASE","DotNetCliVersion":"10.0.403"}""");
        JsonNode benchmark = report["Benchmarks"]![0]!;
        benchmark["DisplayInfo"] = "Fixture.Measure: Core-screen(IterationCount=8)";
        Assert.Throws<ArgumentException>(() => CoreBenchmarkEvaluation.ParseReports([WriteReport(directory.Path, report)], "screen"));
        benchmark["Statistics"]!["OriginalValues"] = new JsonArray(1, 2, 3, 4, 5, 6, 7, 8);
        benchmark["Statistics"]!["N"] = 8;
        benchmark["Measurements"] = new JsonArray(Enumerable.Range(1, 8).Select(index => (JsonNode)new JsonObject
        {
            ["IterationMode"] = "Workload",
            ["IterationStage"] = "Result",
            ["LaunchIndex"] = 1,
            ["IterationIndex"] = index,
            ["Operations"] = 10,
            ["Nanoseconds"] = index * 10,
        }).ToArray());
        Assert.Single(CoreBenchmarkEvaluation.ParseReports([WriteReport(directory.Path, report)], "screen"));
        Assert.Throws<ArgumentException>(() => CoreBenchmarkEvaluation.ParseReports([WriteReport(directory.Path, report)], "diagnose"));
        benchmark["DisplayInfo"] = "Fixture.Measure: Core-diagnose(IterationCount=8)";
        Assert.Single(CoreBenchmarkEvaluation.ParseReports([WriteReport(directory.Path, report)], "diagnose"));
        benchmark["DisplayInfo"] = "Fixture.Measure: Core-compare(LaunchCount=3)";
        Assert.Throws<ArgumentException>(() => CoreBenchmarkEvaluation.ParseReports([WriteReport(directory.Path, report)], "compare"));
        report["HostEnvironmentInfo"]!.AsObject().Remove("DotNetCliVersion");
        Assert.Throws<ArgumentException>(() => CoreBenchmarkEvaluation.ParseReports([WriteReport(directory.Path, report)], "compare"));
    }

    /// <summary>The real Fallout entry point rejects unsupported selection before starting builds.</summary>
    /// <returns>The asynchronous assertion.</returns>
    [Fact]
    public async Task ActualRunnerRejectsInvalidPurposeBeforeBuildAsync()
    {
        using var fixture = new BuildFixture();
        BuildFixture.ProcessResult result = await fixture.RunAsync("BenchmarkCoreEvaluate", "--benchmark-mode", "screen", "--benchmark-category", "Geometry");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("category must be Screening, Comparison, or Diagnostic", result.Output + result.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(fixture.Log));
    }

    /// <summary>The real entry point requires an explicit measurement mode.</summary>
    /// <returns>The asynchronous assertion.</returns>
    [Fact]
    public async Task ActualRunnerRejectsMissingModeAsync()
    {
        using var fixture = new BuildFixture();
        BuildFixture.ProcessResult result = await fixture.RunAsync("BenchmarkCoreEvaluate");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Explicit benchmark mode", result.Output + result.Error, StringComparison.Ordinal);
    }

    private static JsonObject Case() => JsonNode.Parse("""{"Type":"Example.Fixture","Method":"Measure","Parameters":[],"Categories":["Screening"],"Modes":["screen"]}""")!.AsObject();

    private static JsonObject Report() => JsonNode.Parse("""{"Benchmarks":[{"FullName":"Example.Fixture.Measure","Namespace":"Example","Type":"Fixture","Method":"Measure","Parameters":"","Statistics":{"OriginalValues":[1,2,3,4],"N":4},"Memory":{"BytesAllocatedPerOperation":0},"Measurements":[{"IterationMode":"Workload","IterationStage":"Warmup","Operations":1,"Nanoseconds":999999},{"IterationMode":"Workload","IterationStage":"Result","Operations":10,"Nanoseconds":10}]}]}""")!.AsObject();

    private static string WriteReport(string directory, JsonObject report)
    {
        string file = Path.Combine(directory, "fixture-report-full.json");
        File.WriteAllText(file, report.ToJsonString());
        return file;
    }
}
