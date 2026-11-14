using System.Reflection;
using System.Text.Json;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Filters;
using BenchmarkDotNet.Running;
using Lumyte.Benchmarks;
using Xunit;

namespace Lumyte.Core.Benchmark.Tests;

/// <summary>Checks that categories express measurement purpose and subjects use their type hierarchy.</summary>
public sealed class BenchmarkCategoryTests
{
    /// <summary>All Core workload categories are execution purposes, never subject or lifecycle labels.</summary>
    [Fact]
    public void AllWorkloadsDeclareOnlyPurposeCategories()
    {
        foreach (Assembly assembly in CoreCoverageTests.Assemblies)
        {
            using var catalog = JsonDocument.Parse(BenchmarkEntry.CreateCatalog(assembly));
            JsonElement[] cases = catalog.RootElement.GetProperty("Cases").EnumerateArray().ToArray();
            Assert.NotEmpty(cases);
            foreach (JsonElement benchmark in cases)
            {
                string[] categories = Categories(benchmark);
                Assert.NotEmpty(categories);
                Assert.All(categories, category => Assert.Contains(category, new[] { "Screening", "Comparison", "Diagnostic" }));
                Assert.StartsWith(assembly.GetName().Name + ".", benchmark.GetProperty("Type").GetString(), StringComparison.Ordinal);
            }
        }
    }

    /// <summary>Subject selection is an ordinary namespace/class/method glob handled by BDN.</summary>
    [Fact]
    public void MethodFilterSelectsCaptureDirectly()
    {
        Type type = typeof(Lumyte.Composition.Benchmark.BenchmarkChecks).Assembly.GetTypes()
            .Single(candidate => candidate.Name == "CompositionBenchmarks");
        IConfig config = ManualConfig.Create(BenchmarkEntry.CreateConfig("screen"))
            .AddFilter(new GlobFilter(["*CompositionBenchmarks.Capture"]));
        using BenchmarkRunInfo run = BenchmarkConverter.TypeToBenchmarks(type, config);
        Assert.NotEmpty(run.BenchmarksCases);
        Assert.All(run.BenchmarksCases, benchmark => Assert.Equal("Capture", benchmark.Descriptor.WorkloadMethod.Name));
    }

    /// <summary>Diagnostic-only paths retain their purpose without an additional operation category.</summary>
    [Fact]
    public void DiagnosticPurposeDoesNotImplyScreeningOrComparison()
    {
        using var catalog = JsonDocument.Parse(BenchmarkEntry.CreateCatalog(typeof(Lumyte.StateMachines.Benchmark.BenchmarkChecks).Assembly));
        JsonElement[] cases = catalog.RootElement.GetProperty("Cases").EnumerateArray()
            .Where(benchmark => benchmark.GetProperty("Modes").GetArrayLength() == 1).ToArray();
        Assert.Equal(6, cases.Length);
        foreach (JsonElement benchmark in cases)
        {
            Assert.Equal(new[] { "Diagnostic" }, Categories(benchmark));
            Assert.Equal("diagnose", benchmark.GetProperty("Modes")[0].GetString());
        }
    }

    private static string[] Categories(JsonElement benchmark) => benchmark.GetProperty("Categories").EnumerateArray().Select(value => value.GetString()!).ToArray();
}
