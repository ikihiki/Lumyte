using System.Diagnostics;
using System.Text.Json.Nodes;
using Xunit;

namespace Lumyte.Build.Tests;

/// <summary>Managed screening selection, strict data validation and bounded execution contracts.</summary>
public sealed class ComponentScreeningTests
{
    /// <summary>Selection is conservative and preserves explicit uncovered paths.</summary>
    /// <param name="path">Fixture path.</param>
    /// <param name="count">Fixture count.</param>
    /// <param name="uncovered">Fixture uncovered.</param>
    [Theory]
    [InlineData("README.md", 0, false)]
    [InlineData("docs/design.txt", 0, false)]
    [InlineData("global.json", 12, false)]
    [InlineData("Directory.Build.props", 12, false)]
    [InlineData("src/Directory.Build.targets", 12, false)]
    [InlineData("NuGet.config", 12, false)]
    [InlineData("tools/check.cs", 12, false)]
    [InlineData("build/Build.cs", 12, false)]
    [InlineData(".github/workflows/ci.yml", 12, false)]
    [InlineData("src/Core/Lumyte.Signals/Signal.cs", 12, false)]
    [InlineData("benchmarks/Lumyte.Signals.Benchmarks/Scalar.cs", 12, false)]
    [InlineData("benchmarks/Core/Lumyte.Signals.Benchmark/Scalar.cs", 12, false)]
    [InlineData("benchmarks/Core/Shared/BenchmarkEntry.cs", 12, false)]
    [InlineData("src/Graphics/Renderer.cs", 1, true)]
    [InlineData("benchmarks/Core/Lumyte.Mathematics.Benchmark/Program.cs", 1, true)]
    public void Selection(string path, int count, bool uncovered)
    {
        (ScreeningCase[] Cases, string[] Uncovered, string[] Omissions) selection = ComponentScreening.Select([path]);
        Assert.Equal(count, selection.Cases.Length);
        Assert.Equal(uncovered, selection.Uncovered.Contains(path));
    }

    /// <summary>Manual A/A and mixed changes retain capped unique catalog and omissions.</summary>
    [Fact]
    public void AllAndMixedSelection()
    {
        Assert.Empty(ComponentScreening.Select([]).Cases);
        Assert.Equal(12, ComponentScreening.Select([], true).Cases.Length);
        (ScreeningCase[] Cases, string[] Uncovered, string[] Omissions) mixed = ComponentScreening.Select(["src/Core/Lumyte.Signals/Signal.cs", "native/foo.cpp"]);
        Assert.Equal(12, mixed.Cases.DistinctBy(c => c.Id).Count());
        Assert.Equal(["native/foo.cpp"], mixed.Uncovered);
        Assert.Contains(mixed.Omissions, text => text.Contains("Graphics", StringComparison.Ordinal));
    }

    /// <summary>Zero allocation is valid, and both percentage and absolute thresholds are required.</summary>
    /// <param name="median">Fixture median.</param>
    /// <param name="floor">Fixture floor.</param>
    /// <param name="candidate">Fixture candidate.</param>
    [Theory]
    [InlineData(11, 1, true)]
    [InlineData(10.9, 0.1, false)]
    [InlineData(11, 2, false)]
    [InlineData(9, 1, false)]
    public void Threshold(double median, double floor, bool candidate)
    {
        using var temp = new ScreeningTemporaryDirectory();
        JsonObject baseline = ReadFixture(temp.Path, Fixture());
        var head = (JsonObject)baseline.DeepClone();
        head[ComponentScreening.Catalog[0].Id]!["median_ns"] = median;
        JsonNode row = ComponentScreening.Compare(baseline, head, [ComponentScreening.Catalog[0] with { MinDeltaNs = floor }])[0]!;
        Assert.Equal(candidate, row["latency_candidate"]!.GetValue<bool>());
        Assert.Equal(0, baseline[ComponentScreening.Catalog[0].Id]!["allocated_bytes"]!.GetValue<double>());
    }

    /// <summary>Allocation increases are independent, including a zero baseline.</summary>
    [Fact]
    public void AllocationAndRuntimeIdentity()
    {
        using var temp = new ScreeningTemporaryDirectory();
        JsonObject baseline = ReadFixture(temp.Path, Fixture());
        var head = (JsonObject)baseline.DeepClone();
        head[ComponentScreening.Catalog[0].Id]!["allocated_bytes"] = 8.0;
        JsonNode row = ComponentScreening.Compare(baseline, head, [ComponentScreening.Catalog[0]])[0]!;
        Assert.False(row["latency_candidate"]!.GetValue<bool>());
        Assert.True(row["allocation_increase"]!.GetValue<bool>());
        Assert.Null(row["allocation_delta_percent"]);
        head[ComponentScreening.Catalog[0].Id]!["host_identity"]!["RuntimeVersion"] = "other";
        Assert.Equal("incomparable", Assert.Throws<ScreeningFailure>(() => ComponentScreening.Compare(baseline, head, [ComponentScreening.Catalog[0]])).Status);
        Assert.Throws<ScreeningFailure>(() => ComponentScreening.Compare(baseline, [], [ComponentScreening.Catalog[0]]));
    }

    /// <summary>Missing statistics, allocations, identity, job and samples never become a green result.</summary>
    /// <param name="mutation">Fixture mutation.</param>
    [Theory]
    [InlineData("statistics")]
    [InlineData("memory")]
    [InlineData("host")]
    [InlineData("job")]
    [InlineData("parameters")]
    [InlineData("wrong")]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("samples")]
    [InlineData("count")]
    [InlineData("version")]
    [InlineData("configuration")]
    [InlineData("measurement")]
    [InlineData("job-prefix")]
    public void InvalidReports(string mutation)
    {
        using var temp = new ScreeningTemporaryDirectory();
        JsonObject fixture = Fixture();
        JsonNode row = fixture["Benchmarks"]![0]!;
        switch (mutation)
        {
            case "statistics": row["Statistics"] = null; break;
            case "memory": row["Memory"] = null; break;
            case "host": fixture["HostEnvironmentInfo"] = null; break;
            case "job": row["DisplayInfo"] = "Default"; break;
            case "parameters": row["Parameters"] = "Depth=1"; break;
            case "wrong": row["FullName"] = "wrong"; break;
            case "duplicate": fixture["Benchmarks"]!.AsArray().Add(row.DeepClone()); break;
            case "missing": fixture["Benchmarks"]!.AsArray().Clear(); break;
            case "samples": row["Measurements"]!.AsArray().RemoveAt(0); break;
            case "count": row["Statistics"]!["N"] = 7; break;
            case "version": fixture["HostEnvironmentInfo"]!["BenchmarkDotNetVersion"] = "other"; break;
            case "configuration": fixture["HostEnvironmentInfo"]!["Configuration"] = "DEBUG"; break;
            case "job-prefix": row["DisplayInfo"] = row["DisplayInfo"]!.ToString().Replace("IterationCount=8", "IterationCount=80", StringComparison.Ordinal); break;
            case "measurement": row["Measurements"]![0]!["Operations"] = 0; break;
        }

        Assert.Throws<ScreeningFailure>(() => ReadFixture(temp.Path, fixture));
    }

    /// <summary>Boolean, missing, string and non-finite values fail numeric validation.</summary>
    /// <param name="value">Fixture value.</param>
    [Theory]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("-1")]
    [InlineData("0")]
    [InlineData("\"10\"")]
    [InlineData("1e999")]
    public void InvalidNumbers(string value) => Assert.Throws<ScreeningFailure>(() => ComponentScreening.Finite(JsonNode.Parse(value), "median", true));

    /// <summary>Nonfinite managed values fail allocations too.</summary>
    [Fact]
    public void NonfiniteAndMissingFiles()
    {
        Assert.Throws<ScreeningFailure>(() => ComponentScreening.Finite(JsonValue.Create(double.NaN), "allocation"));
        Assert.Throws<ScreeningFailure>(() => ComponentScreening.Finite(JsonValue.Create(double.PositiveInfinity), "allocation"));
        using var temp = new ScreeningTemporaryDirectory();
        Assert.Equal("missing_data", Assert.Throws<ScreeningFailure>(() => ComponentScreening.ReadBdn(temp.Path, [ComponentScreening.Catalog[0]])).Status);
    }

    /// <summary>A shared deadline kills a process tree and preserves partial logs.</summary>
    /// <returns>Completion of deadline assertions.</returns>
    [Fact]
    public async Task DeadlineAndFailureAsync()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new ScreeningTemporaryDirectory();
        var runner = new ComponentScreeningProcess(temp.Path, 0.5);
        ScreeningFailure failure = await Assert.ThrowsAsync<ScreeningFailure>(() => runner.CallAsync("/bin/sh", ["-c", "sleep 30 & echo $! > child.pid; echo partial; wait"], temp.Path, "timeout"));
        Assert.Equal("timeout", failure.Status);
        Assert.Contains("partial", File.ReadAllText(System.IO.Path.Combine(temp.Path, "timeout.log")), StringComparison.Ordinal);
        int pid = int.Parse(File.ReadAllText(System.IO.Path.Combine(temp.Path, "child.pid")), System.Globalization.CultureInfo.InvariantCulture);
        bool alive;
        try
        {
            using var child = Process.GetProcessById(pid);
            alive = !child.HasExited && (!File.Exists("/proc/" + pid + "/stat") || !File.ReadAllText("/proc/" + pid + "/stat").Contains(") Z", StringComparison.Ordinal));
        }
        catch (ArgumentException)
        {
            alive = false;
        }

        Assert.False(alive);
        Assert.Equal(900, new ComponentScreeningProcess(temp.Path, 5000).LimitSeconds);
        var failed = new ComponentScreeningProcess(temp.Path, 5);
        Assert.Equal("execution_error", (await Assert.ThrowsAsync<ScreeningFailure>(() => failed.CallAsync("/bin/sh", ["-c", "exit 4"], temp.Path, "failed"))).Status);
    }

    /// <summary>An exited parent cannot leave output draining beyond the shared deadline.</summary>
    /// <returns>Completion of retained-pipe assertions.</returns>
    [Fact]
    public async Task RetainedPipeDeadlineAsync()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new ScreeningTemporaryDirectory();
        var runner = new ComponentScreeningProcess(temp.Path, 0.5);
        try
        {
            ScreeningFailure failure = await Assert.ThrowsAsync<ScreeningFailure>(() => runner.CallAsync("/bin/sh", ["-c", "sleep 30 & echo $! > orphan.pid; echo partial; exit 0"], temp.Path, "retained"));
            Assert.Equal("timeout", failure.Status);
            Assert.Contains("partial", File.ReadAllText(System.IO.Path.Combine(temp.Path, "retained.log")), StringComparison.Ordinal);
        }
        finally
        {
            string pidFile = System.IO.Path.Combine(temp.Path, "orphan.pid");
            if (File.Exists(pidFile))
            {
                try
                {
                    using var child = Process.GetProcessById(int.Parse(File.ReadAllText(pidFile), System.Globalization.CultureInfo.InvariantCulture));
                    child.Kill(entireProcessTree: true);
                }
                catch (ArgumentException)
                {
                }
            }
        }
    }

    /// <summary>Every ordinary failure writes a final report, including invalid timeouts and plans.</summary>
    /// <returns>Completion of final-report assertions.</returns>
    /// <param name="seconds">Fixture seconds.</param>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(5)]
    public async Task FinallyReportAsync(double seconds)
    {
        using var temp = new ScreeningTemporaryDirectory();
        string plan = System.IO.Path.Combine(temp.Path, "plan.json");
        File.WriteAllText(plan, "{}");
        Assert.False(await ComponentScreening.RunAsync(temp.Path, plan, temp.Path, "dotnet", seconds));
        Assert.True(File.Exists(System.IO.Path.Combine(temp.Path, "result.json")));
        Assert.Contains(seconds > 0 ? "incomparable" : "execution_error", File.ReadAllText(System.IO.Path.Combine(temp.Path, "summary.md")), StringComparison.Ordinal);
    }

    /// <summary>Cleanup removes owned read-only Git objects on Windows without ignoring errors.</summary>
    [Fact]
    public void TemporaryDirectoryDeletesReadOnlyGitObject()
    {
        string root;
        using (var temporary = new ScreeningTemporaryDirectory())
        {
            root = temporary.Path;
            string directory = System.IO.Path.Combine(root, "repository", ".git", "objects", "aa");
            Directory.CreateDirectory(directory);
            string file = System.IO.Path.Combine(directory, "readonly-object");
            File.WriteAllText(file, "owned test object");
            File.SetAttributes(file, File.GetAttributes(file) | FileAttributes.ReadOnly);
            Assert.True((File.GetAttributes(file) & FileAttributes.ReadOnly) != 0);
        }

        Assert.False(Directory.Exists(root));
    }

    /// <summary>Exact two-commit diff includes deletion; snapshots exclude dirty sources and replace the whole harness.</summary>
    /// <returns>Completion of Git integration assertions.</returns>
    [Fact]
    public async Task ExactShaPlanAndSnapshotAsync()
    {
        using var temp = new ScreeningTemporaryDirectory();
        string repo = System.IO.Path.Combine(temp.Path, "repository 日本語");
        Directory.CreateDirectory(repo);
        var runner = new ComponentScreeningProcess(temp.Path, 30);
        async Task<string> GitAsync(params string[] args) => await runner.CallAsync("git", args, repo, "git-" + Guid.NewGuid().ToString("N"));
        await GitAsync("init", "-q");
        await GitAsync("config", "user.email", "fixture@example.invalid");
        await GitAsync("config", "user.name", "fixture");
        string harness = System.IO.Path.Combine(repo, ComponentScreening.Harness);
        Directory.CreateDirectory(harness);
        string shared = System.IO.Path.Combine(repo, ComponentScreening.SharedHarness);
        Directory.CreateDirectory(shared);
        File.WriteAllText(System.IO.Path.Combine(shared, "BenchmarkEntry.cs"), "old shared");
        File.WriteAllText(System.IO.Path.Combine(harness, "stale.cs"), "stale");
        File.WriteAllText(System.IO.Path.Combine(repo, "README.md"), "first");
        File.WriteAllText(System.IO.Path.Combine(repo, "global.json"), "{\"sdk\":{\"version\":\"10.0.403\"}}");
        await GitAsync("add", ".");
        await GitAsync("commit", "-qm", "initial");
        string first = await GitAsync("rev-parse", "HEAD");
        File.WriteAllText(System.IO.Path.Combine(repo, " unknown.cs"), "tracked");
        await GitAsync("add", ".");
        await GitAsync("commit", "-qm", "unknown source");
        string second = await GitAsync("rev-parse", "HEAD");
        File.Delete(System.IO.Path.Combine(harness, "stale.cs"));
        File.WriteAllText(System.IO.Path.Combine(harness, "frozen.cs"), "frozen harness");
        File.WriteAllText(System.IO.Path.Combine(shared, "BenchmarkEntry.cs"), "frozen shared");
        File.WriteAllText(System.IO.Path.Combine(repo, " unknown.cs"), "dirty source");
        string output = System.IO.Path.Combine(temp.Path, "plan output");
        JsonObject plan = await ComponentScreening.CreatePlanAsync(repo, second, first, output, false);
        Assert.Equal(second, plan["base_sha"]!.ToString());
        Assert.Equal(first, plan["head_sha"]!.ToString());
        Assert.Equal(" unknown.cs", Assert.Single(plan["changed_paths"]!.AsArray())!.ToString());
        Assert.Single(ComponentScreening.ValidatePlan(plan, repo));
        string snapshot = System.IO.Path.Combine(temp.Path, "snapshot");
        await ComponentScreening.SnapshotAsync(repo, second, snapshot, harness, runner, temp.Path, "snapshot", shared);
        Assert.Equal("tracked", File.ReadAllText(System.IO.Path.Combine(snapshot, " unknown.cs")));
        Assert.Equal(ComponentScreening.TreeHash(harness), ComponentScreening.TreeHash(System.IO.Path.Combine(snapshot, ComponentScreening.Harness)));
        Assert.Equal("frozen shared", File.ReadAllText(System.IO.Path.Combine(snapshot, ComponentScreening.SharedHarness, "BenchmarkEntry.cs")));
        Assert.Equal(ComponentScreening.HarnessHash(harness, shared), plan["harness_sha256"]!.ToString());
        File.WriteAllText(System.IO.Path.Combine(shared, "BenchmarkEntry.cs"), "changed shared");
        Assert.Equal("incomparable", Assert.Throws<ScreeningFailure>(() => ComponentScreening.ValidatePlan(plan, repo)).Status);
        File.WriteAllText(System.IO.Path.Combine(shared, "BenchmarkEntry.cs"), "frozen shared");
        Assert.False(File.Exists(System.IO.Path.Combine(snapshot, ComponentScreening.Harness, "stale.cs")));
        Assert.True(Directory.Exists(System.IO.Path.Combine(snapshot, "artifacts/nuget")));
        string frozenPlan = System.IO.Path.Combine(output, "plan.json");
        string timeoutOutput = System.IO.Path.Combine(temp.Path, "timeout result");
        Assert.False(await ComponentScreening.RunAsync(repo, frozenPlan, timeoutOutput, "dotnet", 1e-12));
        Assert.Equal("timeout", JsonNode.Parse(File.ReadAllText(System.IO.Path.Combine(timeoutOutput, "result.json")))!["status"]!.ToString());
        string mismatchOutput = System.IO.Path.Combine(temp.Path, "mismatch result");
        Assert.False(await ComponentScreening.RunAsync(repo, frozenPlan, mismatchOutput, "dotnet", 10, headDirectory: repo));
        Assert.Equal("incomparable", JsonNode.Parse(File.ReadAllText(System.IO.Path.Combine(mismatchOutput, "result.json")))!["status"]!.ToString());
        plan["harness_version"] = "legacy";
        Assert.Equal("incomparable", Assert.Throws<ScreeningFailure>(() => ComponentScreening.ValidatePlan(plan, repo)).Status);
        JsonObject skip = await ComponentScreening.CreatePlanAsync(repo, first, first, output, false);
        Assert.False(skip["selected"]!.GetValue<bool>());
        Assert.Contains("skipped", File.ReadAllText(System.IO.Path.Combine(output, "summary.md")), StringComparison.Ordinal);
        File.WriteAllText(System.IO.Path.Combine(repo, "global.json"), "{\"sdk\":{\"version\":\"11.0.100\"}}");
        await GitAsync("add", "global.json");
        await GitAsync("commit", "-qm", "different SDK policy");
        string third = await GitAsync("rev-parse", "HEAD");
        await ComponentScreening.CreatePlanAsync(repo, first, third, output, true);
        string sdkOutput = System.IO.Path.Combine(temp.Path, "SDK mismatch result");
        Assert.False(await ComponentScreening.RunAsync(repo, frozenPlan, sdkOutput, "dotnet", 10));
        Assert.Contains("global.json SDK policies differ", File.ReadAllText(System.IO.Path.Combine(sdkOutput, "result.json")), StringComparison.Ordinal);
    }

    /// <summary>The workflow bootstraps SDK before managed validation/planning and has no shell engine orchestration.</summary>
    [Fact]
    public void WorkflowContract()
    {
        string root = RepositoryRoot();
        string workflow = File.ReadAllText(System.IO.Path.Combine(root, ".github/workflows/component-screening.yml"));
        Assert.True(workflow.IndexOf("actions/setup-dotnet", StringComparison.Ordinal) < workflow.IndexOf("--target ComponentScreeningPlan", StringComparison.Ordinal));
        Assert.Contains("FullyQualifiedName~ComponentScreening", workflow, StringComparison.Ordinal);
        Assert.Contains("--target ComponentScreeningRun", workflow, StringComparison.Ordinal);
        Assert.Contains("if: always()", workflow, StringComparison.Ordinal);
        Assert.Contains("persist-credentials: false", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("python", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("git worktree", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("--screening-base-directory", workflow, StringComparison.Ordinal);
        foreach (string component in new[] { "Signals", "Mathematics" })
        {
            string project = System.IO.Path.Combine(root, "benchmarks/Core/Lumyte." + component + ".Benchmark/Lumyte." + component + ".Benchmark.csproj");
            Assert.True(File.Exists(project));
            Assert.Contains("../../../src/", File.ReadAllText(project), StringComparison.Ordinal);
        }
    }

    private static string RepositoryRoot()
    {
        string? path = AppContext.BaseDirectory;
        while (path is not null && !File.Exists(System.IO.Path.Combine(path, "Lumyte.slnx")))
        {
            path = System.IO.Path.GetDirectoryName(path);
        }

        return path ?? throw new InvalidOperationException("Repository not found");
    }

    private static JsonObject ReadFixture(string directory, JsonObject fixture)
    {
        File.WriteAllText(System.IO.Path.Combine(directory, "fixture-full.json"), fixture.ToJsonString());
        return ComponentScreening.ReadBdn(directory, [ComponentScreening.Catalog[0]]);
    }

    private static JsonObject Fixture() => JsonNode.Parse("""
        {"HostEnvironmentInfo":{"BenchmarkDotNetVersion":"0.15.8","OsVersion":"fixture OS","ProcessorName":"fixture CPU","Architecture":"X64","RuntimeVersion":".NET 10.0","Configuration":"RELEASE","DotNetCliVersion":"10.0.403"},
        "Benchmarks":[{"FullName":"Lumyte.Signals.Benchmark.ScalarBenchmarks.ReadValue","Parameters":"","HardwareIntrinsics":"fixture",
        "DisplayInfo":"SignalsScreening-v2(OutlierMode=DontRemove, IterationCount=8, IterationTime=150ms, LaunchCount=1, WarmupCount=3)",
        "Statistics":{"Median":10,"Mean":10,"StandardDeviation":0.1,"N":8},"Memory":{"BytesAllocatedPerOperation":0},"Measurements":[]}]}
        """)!.AsObject().WithMeasurements();
}
