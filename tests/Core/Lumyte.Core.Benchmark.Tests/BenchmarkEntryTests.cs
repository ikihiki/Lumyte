using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Filters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Running;
using Lumyte.Benchmarks;
using Perfolizer.Horology;
using Perfolizer.Mathematics.OutlierDetection;
using Xunit;

namespace Lumyte.Core.Benchmark.Tests;

/// <summary>Checks mode policy without running timed benchmarks.</summary>
public sealed class BenchmarkEntryTests
{
    /// <summary>Explicit project binding retains the standard SDK builder and executor.</summary>
    [Fact]
    public void ExplicitProjectToolchainRetainsStandardExecution()
    {
        string root = Path.Combine(Path.GetTempPath(), "benchmark project " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string project = Path.Combine(root, "Lumyte.Core.Benchmark.Tests.csproj");
            File.WriteAllText(project, "<Project />");
            IConfig config = BenchmarkEntry.CreateConfig("screen", project);
            BenchmarkEntry.ProjectToolchain toolchain = Assert.IsType<BenchmarkEntry.ProjectToolchain>(Assert.Single(config.GetJobs()).Infrastructure.Toolchain);
            BenchmarkEntry.ProjectGenerator generator = Assert.IsType<BenchmarkEntry.ProjectGenerator>(toolchain.Generator);
            Assert.Equal(project, generator.Project.FullName);
            Assert.Same(BenchmarkDotNet.Toolchains.CsProj.CsProjCoreToolchain.NetCoreApp10_0.Builder, toolchain.Builder);
            Assert.Same(BenchmarkDotNet.Toolchains.CsProj.CsProjCoreToolchain.NetCoreApp10_0.Executor, toolchain.Executor);
            Assert.Throws<ArgumentException>(() => BenchmarkEntry.CreateConfig("screen", Path.Combine(root, "missing.csproj")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>New executables default to catalog discovery without selecting a measurement mode.</summary>
    [Fact]
    public void DefaultsToCatalog()
    {
        BenchmarkEntry.EntryOptions options = BenchmarkEntry.Parse([]);
        Assert.Null(options.Mode);
        Assert.True(options.Catalog);
        Assert.Empty(options.Arguments);
    }

    /// <summary>Informational options do not invent an execution mode or implicit catalog flag.</summary>
    /// <param name="args">Informational command-line arguments.</param>
    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("--list", "flat")]
    [InlineData("--list", "tree", "--filter", "*Untagged*")]
    public void InformationalParsingLeavesModeUnspecified(params string[] args)
    {
        BenchmarkEntry.EntryOptions options = BenchmarkEntry.Parse(args);
        Assert.Null(options.Mode);
        Assert.False(options.Catalog);
        Assert.Equal(args, options.Arguments);
    }

    /// <summary>Explicit catalog discovery includes the full assembly even when a mode was supplied.</summary>
    [Fact]
    public void ExplicitCatalogIsIndependentOfMode()
    {
        BenchmarkEntry.EntryOptions options = BenchmarkEntry.Parse(["--mode", "screen", "--catalog"]);
        Assert.Equal("screen", options.Mode);
        Assert.True(options.Catalog);
        Assert.Empty(options.Arguments);
        Assert.Null(BenchmarkEntry.Parse(["--catalog"]).Mode);
    }

    /// <summary>Shared switches are removed without changing BDN filter or parameter options.</summary>
    /// <param name="mode">The requested execution mode.</param>
    [Theory]
    [InlineData("screen")]
    [InlineData("compare")]
    [InlineData("diagnose")]
    public void ModeKeepsBenchmarkDotNetArguments(string mode)
    {
        string[] remaining = ["--filter", "*Work*", "--allCategories", "Numeric", "--join", "--list", "flat"];
        BenchmarkEntry.EntryOptions options = BenchmarkEntry.Parse(["--mode", mode, .. remaining]);
        Assert.Equal(mode, options.Mode);
        Assert.Equal(remaining, options.Arguments);
        Assert.Equal(mode, BenchmarkEntry.Parse([$"--mode={mode}"]).Mode);
    }

    /// <summary>Invalid shared switches cannot silently select a fallback mode.</summary>
    /// <param name="args">Invalid command-line arguments.</param>
    [Theory]
    [InlineData("--mode")]
    [InlineData("--mode=")]
    [InlineData("--mode", "--catalog")]
    [InlineData("--mode", "unknown")]
    [InlineData("--mode", "Screen")]
    [InlineData("--mode", "screen", "--mode=compare")]
    [InlineData("--catalog", "--catalog")]
    [InlineData("--catalog", "--filter", "*")]
    [InlineData("--configuration", "Debug")]
    [InlineData("--inProcess")]
    [InlineData("--disableOptimizationsValidator")]
    public void RejectsInvalidArguments(params string[] args) => Assert.Throws<ArgumentException>(() => BenchmarkEntry.Parse(args));

    /// <summary>Removed verification modes and implicit measurement requests fail without executing work.</summary>
    /// <param name="args">Unsupported execution arguments.</param>
    [Theory]
    [InlineData("--mode", "verify")]
    [InlineData("--mode=verify")]
    [InlineData("--mode", "validate")]
    [InlineData("--filter", "*")]
    [InlineData("--mode", "invalid")]
    public void UnsupportedExecutionReturnsFailure(params string[] args)
    {
        Assert.Throws<ArgumentException>(() => BenchmarkEntry.Parse(args));
        Assert.Equal(1, BenchmarkEntry.Run(args, typeof(BenchmarkEntryTests).Assembly));
    }

    /// <summary>Default catalog, help and listing never execute benchmark workloads.</summary>
    /// <param name="args">Informational command-line arguments.</param>
    [Theory]
    [InlineData]
    [InlineData("--catalog")]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("--list", "flat")]
    [InlineData("--list", "tree")]
    [InlineData("--mode", "diagnose", "--list", "flat")]
    public void InformationalCommandsDoNotExecuteWork(params string[] args)
    {
        int result = BenchmarkEntry.Run(
            args,
            typeof(CatalogFixture).Assembly);
        Assert.Equal(0, result);
    }

    /// <summary>An unmatched filter is not a successful measurement.</summary>
    [Fact]
    public void EmptyBenchmarkSelectionFails()
    {
        int result = BenchmarkEntry.Run(
            ["--mode", "screen", "--filter", "*NoSuchMethod_837*"],
            typeof(CatalogFixture).Assembly);
        Assert.Equal(1, result);
    }

    /// <summary>Profiler stream failures logged by BDN cannot be reported as successful diagnosis.</summary>
    [Fact]
    public void ErrorLoggerRecordsProfilerErrors()
    {
        var logger = new BenchmarkEntry.ErrorLogger();
        logger.WriteLine(LogKind.Info, "not an error");
        Assert.False(logger.HasErrors);
        logger.WriteLine(LogKind.Error, "trace stream failure");
        Assert.True(logger.HasErrors);
    }

    /// <summary>Screen settings encode the short job and leave product JIT/GC policy unspecified.</summary>
    [Fact]
    public void ScreenHasRealJobCharacteristics()
    {
        Job job = BenchmarkEntry.CreateJob("screen");
        Assert.Equal("Release", job.Infrastructure.BuildConfiguration);
        Assert.Equal(1, job.Run.LaunchCount);
        Assert.Equal(3, job.Run.WarmupCount);
        Assert.Equal(8, job.Run.IterationCount);
        Assert.Equal(TimeInterval.FromMilliseconds(150), job.Run.IterationTime);
        Assert.Equal(OutlierMode.DontRemove, job.Accuracy.OutlierMode);
        Assert.False(job.Environment.HasValue(EnvironmentMode.JitCharacteristic));
        Assert.False(job.Environment.HasValue(EnvironmentMode.EnvironmentVariablesCharacteristic));
        Assert.False(job.Environment.Gc.HasValue(GcMode.ServerCharacteristic));
        Assert.False(job.Environment.Gc.HasValue(GcMode.ConcurrentCharacteristic));
    }

    /// <summary>Comparison uses independent launches with adaptive BDN warmup and measurement.</summary>
    [Fact]
    public void CompareDoesNotPinAdaptiveCounts()
    {
        Job job = BenchmarkEntry.CreateJob("compare");
        Assert.Equal(3, job.Run.LaunchCount);
        Assert.False(job.Run.HasValue(BenchmarkDotNet.Jobs.RunMode.WarmupCountCharacteristic));
        Assert.False(job.Run.HasValue(BenchmarkDotNet.Jobs.RunMode.IterationCountCharacteristic));
        Assert.False(job.Run.HasValue(BenchmarkDotNet.Jobs.RunMode.IterationTimeCharacteristic));
        Assert.Equal("Release", job.Infrastructure.BuildConfiguration);
    }

    /// <summary>All measured modes export complete JSON and managed allocation measurements.</summary>
    /// <param name="mode">The requested measurement mode.</param>
    [Theory]
    [InlineData("screen")]
    [InlineData("compare")]
    [InlineData("diagnose")]
    public void ConfigIncludesStandardArtifacts(string mode)
    {
        IConfig config = BenchmarkEntry.CreateConfig(mode);
        Assert.Single(config.GetJobs());
        Assert.Contains(config.GetDiagnosers(), diagnoser => diagnoser is MemoryDiagnoser);
        Assert.Contains(JsonExporter.Full, config.GetExporters());
        Assert.Contains(config.GetValidators(), validator => validator.TreatsWarningsAsErrors);
        EventPipeProfiler[] profilers = config.GetDiagnosers().OfType<EventPipeProfiler>().ToArray();
        if (mode == "diagnose")
        {
            EventPipeProfiler profiler = Assert.Single(profilers);
            Assert.Equal(BenchmarkDotNet.Diagnosers.RunMode.ExtraRun, profiler.GetRunMode(null!));
        }
        else
        {
            Assert.Empty(profilers);
        }
    }

    /// <summary>Mandatory mode policy rejects actual diagnostic-only cases even when CLI filters select them.</summary>
    /// <param name="mode">The selected measurement mode.</param>
    /// <param name="included">Whether diagnostic-only cases may run.</param>
    [Theory]
    [InlineData("screen", false)]
    [InlineData("compare", false)]
    [InlineData("diagnose", true)]
    public void DiagnosticOnlyFilterCannotBeOverridden(string mode, bool included)
    {
        using BenchmarkRunInfo all = BenchmarkConverter.TypeToBenchmarks(typeof(CatalogFixture), BenchmarkEntry.CreateConfig("diagnose"));
        BenchmarkCase diagnostic = all.BenchmarksCases.First(benchmark => benchmark.Descriptor.WorkloadMethod.Name == nameof(CatalogFixture.DiagnosticWork));
        BenchmarkCase normal = all.BenchmarksCases.First(benchmark => benchmark.Descriptor.WorkloadMethod.Name == nameof(CatalogFixture.Work));
        IFilter policy = Assert.Single(BenchmarkEntry.CreateConfig(mode).GetFilters());
        Assert.Equal(included, policy.Predicate(diagnostic));
        Assert.True(policy.Predicate(normal));
        Assert.False(new BenchmarkEntry.ModeFilter("verify").Predicate(diagnostic));

        ManualConfig selected = ManualConfig.Create(BenchmarkEntry.CreateConfig(mode))
            .AddFilter(new GlobFilter(["*DiagnosticWork*"]))
            .AddFilter(new AnyCategoriesFilter(["diagnostic-only"]));
        using BenchmarkRunInfo filtered = BenchmarkConverter.TypeToBenchmarks(typeof(CatalogFixture), selected);
        Assert.Equal(included ? 2 : 0, filtered.BenchmarksCases.Length);
        Assert.All(filtered.BenchmarksCases, benchmark => Assert.Equal(nameof(CatalogFixture.DiagnosticWork), benchmark.Descriptor.WorkloadMethod.Name));
    }

    /// <summary>Only explicitly declared purposes admit measurement, including inherited class categories.</summary>
    /// <param name="fixture">The benchmark fixture type.</param>
    /// <param name="method">The selected method.</param>
    /// <param name="expectedModes">The eligible modes in catalog order.</param>
    [Theory]
    [InlineData(typeof(UncategorizedFixture), nameof(UncategorizedFixture.Work), new string[0])]
    [InlineData(typeof(PurposeFixture), nameof(PurposeFixture.Untagged), new string[0])]
    [InlineData(typeof(PurposeFixture), nameof(PurposeFixture.Screen), new[] { "screen" })]
    [InlineData(typeof(PurposeFixture), nameof(PurposeFixture.Compare), new[] { "compare" })]
    [InlineData(typeof(PurposeFixture), nameof(PurposeFixture.Diagnose), new[] { "diagnose" })]
    [InlineData(typeof(PurposeFixture), nameof(PurposeFixture.Multiple), new[] { "screen", "compare" })]
    [InlineData(typeof(PurposeFixture), nameof(PurposeFixture.LegacyMarker), new string[0])]
    [InlineData(typeof(InheritedPurposeFixture), nameof(InheritedPurposeFixture.Work), new[] { "screen", "diagnose" })]
    public void PurposeCategoriesDetermineModes(Type fixture, string method, string[] expectedModes)
    {
        // Do not apply the mode filter while discovering fixtures: cases without measurement purposes must remain visible.
        using BenchmarkRunInfo all = BenchmarkConverter.TypeToBenchmarks(fixture, DefaultConfig.Instance);
        BenchmarkCase benchmark = Assert.Single(all.BenchmarksCases, item => item.Descriptor.WorkloadMethod.Name == method);
        Assert.Equal(expectedModes, BenchmarkEntry.GetModes(benchmark));
        foreach (string mode in new[] { "screen", "compare", "diagnose" })
        {
            Assert.Equal(expectedModes.Contains(mode), new BenchmarkEntry.ModeFilter(mode).Predicate(benchmark));
        }

        using var catalog = JsonDocument.Parse(BenchmarkEntry.CreateCatalog(fixture.Assembly));
        JsonElement entry = Assert.Single(catalog.RootElement.GetProperty("Cases").EnumerateArray(), item =>
            item.GetProperty("Type").GetString() == fixture.FullName && item.GetProperty("Method").GetString() == method);
        Assert.Equal(expectedModes, entry.GetProperty("Modes").EnumerateArray().Select(mode => mode.GetString()!).ToArray());
    }

    /// <summary>Literal BDN method selection and category filters cannot override required purposes.</summary>
    /// <param name="mode">The requested measurement mode.</param>
    /// <param name="method">The literal workload method name.</param>
    /// <param name="included">Whether the selected purpose admits that mode.</param>
    [Theory]
    [InlineData("screen", nameof(PurposeFixture.Screen), true)]
    [InlineData("compare", nameof(PurposeFixture.Screen), false)]
    [InlineData("diagnose", nameof(PurposeFixture.Screen), false)]
    [InlineData("screen", nameof(PurposeFixture.Untagged), false)]
    [InlineData("compare", nameof(PurposeFixture.Untagged), false)]
    [InlineData("diagnose", nameof(PurposeFixture.Untagged), false)]
    public void LiteralMethodFiltersRespectPurposePolicy(string mode, string method, bool included)
    {
        string literal = $"{typeof(PurposeFixture).FullName}.{method}";
        Assert.Equal(new[] { "--filter", literal }, BenchmarkEntry.Parse(["--mode", mode, "--filter", literal]).Arguments);
        using BenchmarkRunInfo all = BenchmarkConverter.TypeToBenchmarks(typeof(PurposeFixture), DefaultConfig.Instance);
        BenchmarkCase selected = Assert.Single(all.BenchmarksCases, benchmark => benchmark.Descriptor.WorkloadMethod.Name == method);
        var glob = new GlobFilter([literal]);
        Assert.True(glob.Predicate(selected));
        Assert.Single(all.BenchmarksCases, glob.Predicate);

        ManualConfig config = ManualConfig.Create(BenchmarkEntry.CreateConfig(mode))
            .AddFilter(glob)
            .AddFilter(new AnyCategoriesFilter(["Semantic"]));
        using BenchmarkRunInfo filtered = BenchmarkConverter.TypeToBenchmarks(typeof(PurposeFixture), config);
        Assert.Equal(included ? 1 : 0, filtered.BenchmarksCases.Length);
        Assert.All(filtered.BenchmarksCases, benchmark => Assert.Equal(method, benchmark.Descriptor.WorkloadMethod.Name));
    }

    /// <summary>Catalog uses actual BDN descriptors and expands every declared parameter.</summary>
    [Fact]
    public void CatalogContainsRealMethodsCategoriesParametersAndIdentity()
    {
        using var document = JsonDocument.Parse(BenchmarkEntry.CreateCatalog(typeof(CatalogFixture).Assembly));
        JsonElement root = document.RootElement;
        Assert.Equal(1, root.GetProperty("SchemaVersion").GetInt32());
        Assert.Equal(
            new[] { "screen", "compare", "diagnose" },
            root.GetProperty("Modes").EnumerateArray().Select(mode => mode.GetString()!).ToArray());
        Assert.Equal(
            typeof(CatalogFixture).Assembly.GetName().Name,
            root.GetProperty("Assembly").GetProperty("Name").GetString());
        Assert.Equal(
            typeof(CatalogFixture).Assembly.ManifestModule.ModuleVersionId.ToString(),
            root.GetProperty("Assembly").GetProperty("ModuleVersionId").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("Runtime").GetProperty("Framework").GetString()));
        JsonElement[] cases = root.GetProperty("Cases").EnumerateArray()
            .Where(item => item.GetProperty("Type").GetString() == typeof(CatalogFixture).FullName
                && item.GetProperty("Method").GetString() == nameof(CatalogFixture.Work)).ToArray();
        JsonElement[] diagnosticCases = root.GetProperty("Cases").EnumerateArray()
            .Where(item => item.GetProperty("Type").GetString() == typeof(CatalogFixture).FullName
                && item.GetProperty("Method").GetString() == nameof(CatalogFixture.DiagnosticWork)).ToArray();
        Assert.Equal(2, diagnosticCases.Length);
        foreach (JsonElement item in diagnosticCases)
        {
            Assert.Contains("diagnostic-only", item.GetProperty("Categories").EnumerateArray().Select(category => category.GetString()));
            Assert.Equal(new[] { "diagnose" }, item.GetProperty("Modes").EnumerateArray().Select(mode => mode.GetString()!).ToArray());
        }

        Assert.Equal(4, cases.Length);
        foreach (JsonElement item in cases)
        {
            Assert.Equal(nameof(CatalogFixture.Work), item.GetProperty("Method").GetString());
            Assert.Contains("Catalog", item.GetProperty("Categories").EnumerateArray().Select(category => category.GetString()));
            Assert.Equal(3, item.GetProperty("Modes").GetArrayLength());
            Assert.Equal(2, item.GetProperty("Parameters").GetArrayLength());
            Assert.Contains(item.GetProperty("Parameters").EnumerateArray(), parameter =>
                parameter.GetProperty("Name").GetString() == "Count" && !parameter.GetProperty("IsArgument").GetBoolean());
            Assert.Contains(item.GetProperty("Parameters").EnumerateArray(), parameter =>
                parameter.GetProperty("Name").GetString() == "value" && parameter.GetProperty("IsArgument").GetBoolean());
        }
    }

    /// <summary>Benchmarks without any categories remain catalog-visible but cannot be measured.</summary>
    public class UncategorizedFixture
    {
        /// <summary>Declares no categories or measurement purposes.</summary>
        [Benchmark]
        public void Work() => throw new InvalidOperationException("Unexpected workload execution.");
    }

    /// <summary>Semantic tags alone must not grant measurement eligibility.</summary>
    [BenchmarkCategory("Semantic")]
    public class PurposeFixture
    {
        /// <summary>Declares no measurement purpose.</summary>
        [Benchmark]
        public void Untagged() => throw new InvalidOperationException("Unexpected workload execution.");

        /// <summary>Declares screening only.</summary>
        [Benchmark]
        [BenchmarkCategory("Screening")]
        public void Screen() => throw new InvalidOperationException("Unexpected workload execution.");

        /// <summary>Declares comparison only.</summary>
        [Benchmark]
        [BenchmarkCategory("Comparison")]
        public void Compare() => throw new InvalidOperationException("Unexpected workload execution.");

        /// <summary>Declares diagnosis only.</summary>
        [Benchmark]
        [BenchmarkCategory("Diagnostic")]
        public void Diagnose() => throw new InvalidOperationException("Unexpected workload execution.");

        /// <summary>Combines independent measurement purposes.</summary>
        [Benchmark]
        [BenchmarkCategory("Screening", "Comparison")]
        public void Multiple() => throw new InvalidOperationException("Unexpected workload execution.");

        /// <summary>The compatibility exclusion marker does not itself grant any purpose.</summary>
        [Benchmark]
        [BenchmarkCategory("diagnostic-only")]
        public void LegacyMarker() => throw new InvalidOperationException("Unexpected workload execution.");
    }

    /// <summary>Provides a purpose category inherited by a concrete benchmark class.</summary>
    [BenchmarkCategory("Screening")]
    public abstract class PurposeBase;

    /// <summary>Combines inherited class and local method purposes through BDN descriptors.</summary>
    public class InheritedPurposeFixture : PurposeBase
    {
        /// <summary>Declares a second purpose at method level.</summary>
        [Benchmark]
        [BenchmarkCategory("Diagnostic")]
        public void Work() => throw new InvalidOperationException("Unexpected workload execution.");
    }

    /// <summary>Real benchmark metadata for catalog discovery; any workload execution is a test failure.</summary>
    [BenchmarkCategory("Catalog")]
    public class CatalogFixture
    {
        /// <summary>Gets or sets an independently expanded workload parameter.</summary>
        [Params(1, 4)]
        public int Count { get; set; }

        /// <summary>Provides real argument metadata without allowing any measurement.</summary>
        /// <param name="value">The argument selected by BDN.</param>
        /// <returns>Never returns; catalog generation must not execute this method.</returns>
        [Benchmark]
        [Arguments(2)]
        [Arguments(3)]
        [BenchmarkCategory("Screening", "Comparison", "Diagnostic")]
        public int Work(int value) => throw new InvalidOperationException($"Catalog executed {value}.");

        /// <summary>Supplies diagnostic-only metadata without permitting workload execution in tests.</summary>
        /// <returns>Never returns; tests enumerate metadata only.</returns>
        [Benchmark]
        [BenchmarkCategory("Screening", "Comparison", "Diagnostic", "diagnostic-only")]
        public int DiagnosticWork() => throw new InvalidOperationException("Diagnostic workload executed.");
    }
}
