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
    /// <summary>New executables default to untimed correctness verification.</summary>
    [Fact]
    public void DefaultsToVerify()
    {
        BenchmarkEntry.EntryOptions options = BenchmarkEntry.Parse([]);
        Assert.Equal("verify", options.Mode);
        Assert.False(options.Catalog);
        Assert.Empty(options.Arguments);
    }

    /// <summary>Shared switches are removed without changing BDN filter or parameter options.</summary>
    /// <param name="mode">The requested execution mode.</param>
    [Theory]
    [InlineData("verify")]
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

    /// <summary>Verification calls the callback once and reports failures through a return code.</summary>
    [Fact]
    public void VerificationReturnsFailureWithoutExiting()
    {
        int calls = 0;
        Assert.Equal(0, BenchmarkEntry.Run([], typeof(BenchmarkEntryTests).Assembly, () => calls++));
        Assert.Equal(1, calls);
        int failure = BenchmarkEntry.Run(
            ["--mode", "verify"],
            typeof(BenchmarkEntryTests).Assembly,
            () => throw new InvalidOperationException("expected verification failure"));
        Assert.Equal(1, failure);
        Assert.Equal(1, BenchmarkEntry.Run(["--mode", "invalid"], typeof(BenchmarkEntryTests).Assembly, () => calls++));
        Assert.Equal(1, calls);
    }

    /// <summary>Catalog, help and flat listing bypass correctness and measurement execution.</summary>
    /// <param name="args">Informational command-line arguments.</param>
    [Theory]
    [InlineData("--catalog")]
    [InlineData("--help")]
    [InlineData("--list", "flat")]
    public void InformationalCommandsDoNotExecuteWork(params string[] args)
    {
        int result = BenchmarkEntry.Run(
            args,
            typeof(CatalogFixture).Assembly,
            () => throw new InvalidOperationException("Information command invoked verification."));
        Assert.Equal(0, result);
    }

    /// <summary>An unmatched filter is not a successful measurement.</summary>
    [Fact]
    public void EmptyBenchmarkSelectionFails()
    {
        int result = BenchmarkEntry.Run(
            ["--mode", "screen", "--filter", "*NoSuchMethod_837*"],
            typeof(CatalogFixture).Assembly,
            () => throw new InvalidOperationException("Unexpected verification."));
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
        Assert.True(new BenchmarkEntry.ModeFilter("verify").Predicate(diagnostic));

        ManualConfig selected = ManualConfig.Create(BenchmarkEntry.CreateConfig(mode))
            .AddFilter(new GlobFilter(["*DiagnosticWork*"]))
            .AddFilter(new AnyCategoriesFilter(["diagnostic-only"]));
        using BenchmarkRunInfo filtered = BenchmarkConverter.TypeToBenchmarks(typeof(CatalogFixture), selected);
        Assert.Equal(included ? 2 : 0, filtered.BenchmarksCases.Length);
        Assert.All(filtered.BenchmarksCases, benchmark => Assert.Equal(nameof(CatalogFixture.DiagnosticWork), benchmark.Descriptor.WorkloadMethod.Name));
    }

    /// <summary>Catalog uses actual BDN descriptors and expands every declared parameter.</summary>
    [Fact]
    public void CatalogContainsRealMethodsCategoriesParametersAndIdentity()
    {
        using var document = JsonDocument.Parse(BenchmarkEntry.CreateCatalog(typeof(CatalogFixture).Assembly));
        JsonElement root = document.RootElement;
        Assert.Equal(1, root.GetProperty("SchemaVersion").GetInt32());
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
            Assert.Equal(new[] { "verify", "diagnose" }, item.GetProperty("Modes").EnumerateArray().Select(mode => mode.GetString()!).ToArray());
        }

        Assert.Equal(4, cases.Length);
        foreach (JsonElement item in cases)
        {
            Assert.Equal(nameof(CatalogFixture.Work), item.GetProperty("Method").GetString());
            Assert.Contains("Catalog", item.GetProperty("Categories").EnumerateArray().Select(category => category.GetString()));
            Assert.Equal(4, item.GetProperty("Modes").GetArrayLength());
            Assert.Equal(2, item.GetProperty("Parameters").GetArrayLength());
            Assert.Contains(item.GetProperty("Parameters").EnumerateArray(), parameter =>
                parameter.GetProperty("Name").GetString() == "Count" && !parameter.GetProperty("IsArgument").GetBoolean());
            Assert.Contains(item.GetProperty("Parameters").EnumerateArray(), parameter =>
                parameter.GetProperty("Name").GetString() == "value" && parameter.GetProperty("IsArgument").GetBoolean());
        }
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
        public int Work(int value) => throw new InvalidOperationException($"Catalog executed {value}.");

        /// <summary>Supplies diagnostic-only metadata without permitting workload execution in tests.</summary>
        /// <returns>Never returns; tests enumerate metadata only.</returns>
        [Benchmark]
        [BenchmarkCategory("diagnostic-only")]
        public int DiagnosticWork() => throw new InvalidOperationException("Diagnostic workload executed.");
    }
}
