using System.Reflection;
using System.Text.Json;
using Lumyte.Benchmarks;
using Xunit;

namespace Lumyte.Core.Benchmark.Tests;

/// <summary>Checks runtime category metadata independently of source-file placement.</summary>
public sealed class BenchmarkCategoryTests
{
    /// <summary>Every executable exposes its ownership categories through BenchmarkDotNet metadata.</summary>
    [Fact]
    public void AllWorkloadsDeclareOwnershipCategories()
    {
        Assembly[] assemblies =
        [
            typeof(Lumyte.Core.Benchmark.BenchmarkChecks).Assembly,
            typeof(Lumyte.Mathematics.Benchmark.BenchmarkChecks).Assembly,
            typeof(Lumyte.Memory.Benchmark.BenchmarkChecks).Assembly,
            typeof(Lumyte.TextEditing.Benchmark.BenchmarkChecks).Assembly,
            typeof(Lumyte.Signals.Benchmark.BenchmarkChecks).Assembly,
            typeof(Lumyte.Resources.Benchmark.BenchmarkChecks).Assembly,
            typeof(Lumyte.Settings.Benchmark.BenchmarkChecks).Assembly,
            typeof(Lumyte.Execution.Benchmark.BenchmarkChecks).Assembly,
            typeof(Lumyte.Composition.Benchmark.BenchmarkChecks).Assembly,
            typeof(Lumyte.Execution.Generators.Benchmark.BenchmarkChecks).Assembly,
            typeof(Lumyte.Composition.Generators.Benchmark.BenchmarkChecks).Assembly,
            typeof(Lumyte.StateMachines.Benchmark.BenchmarkChecks).Assembly,
            typeof(Lumyte.Worlds.Benchmark.BenchmarkChecks).Assembly,
        ];
        foreach (Assembly assembly in assemblies)
        {
            string owner = assembly.GetName().Name!["Lumyte.".Length..^".Benchmark".Length];
            using var catalog = JsonDocument.Parse(BenchmarkEntry.CreateCatalog(assembly));
            JsonElement[] cases = catalog.RootElement.GetProperty("Cases").EnumerateArray().ToArray();
            Assert.NotEmpty(cases);
            Assert.All(cases, benchmark =>
            {
                string[] categories = Categories(benchmark);
                Assert.Contains("Core", categories);
                Assert.Contains(owner, categories);
            });
        }
    }

    /// <summary>One semantic category selects several independent fixture classes.</summary>
    [Fact]
    public void GeometryCategorySpansIndependentFixtures()
    {
        using var catalog = JsonDocument.Parse(BenchmarkEntry.CreateCatalog(typeof(Lumyte.Mathematics.Benchmark.BenchmarkChecks).Assembly));
        string[] types = catalog.RootElement.GetProperty("Cases").EnumerateArray()
            .Where(benchmark => Categories(benchmark).Contains("Geometry", StringComparer.Ordinal))
            .Select(benchmark => benchmark.GetProperty("Type").GetString()!).Distinct(StringComparer.Ordinal).ToArray();
        Assert.Contains("Lumyte.Mathematics.Benchmark.BatchBenchmark", types);
        Assert.Contains("Lumyte.Mathematics.Benchmark.BoundsBenchmarks", types);
        Assert.Contains("Lumyte.Mathematics.Benchmark.GeometryBenchmarks", types);
    }

    /// <summary>Different operations in the same fixture retain independent semantic categories.</summary>
    [Fact]
    public void OneFixtureHasMultipleOperationCategories()
    {
        using var catalog = JsonDocument.Parse(BenchmarkEntry.CreateCatalog(typeof(Lumyte.Execution.Benchmark.BenchmarkChecks).Assembly));
        JsonElement[] cases = catalog.RootElement.GetProperty("Cases").EnumerateArray()
            .Where(benchmark => benchmark.GetProperty("Type").GetString() == "Lumyte.Execution.Benchmark.ExecutionBenchmarks").ToArray();
        foreach ((string method, string category) in new[] { ("Single", "Commands"), ("Graph", "Graphs"), ("Workflow", "Workflows") })
        {
            JsonElement[] selected = cases.Where(benchmark => benchmark.GetProperty("Method").GetString() == method).ToArray();
            Assert.NotEmpty(selected);
            Assert.All(selected, benchmark => Assert.Contains(category, Categories(benchmark)));
        }
    }

    /// <summary>Cross-project generator selection uses the shared category rather than project or file names.</summary>
    [Fact]
    public void SourceGenerationCategorySpansBothOwners()
    {
        foreach (Assembly assembly in new[] { typeof(Lumyte.Execution.Generators.Benchmark.BenchmarkChecks).Assembly, typeof(Lumyte.Composition.Generators.Benchmark.BenchmarkChecks).Assembly })
        {
            using var catalog = JsonDocument.Parse(BenchmarkEntry.CreateCatalog(assembly));
            Assert.All(catalog.RootElement.GetProperty("Cases").EnumerateArray(), benchmark => Assert.Contains("SourceGeneration", Categories(benchmark)));
        }
    }

    private static string[] Categories(JsonElement benchmark) => benchmark.GetProperty("Categories").EnumerateArray().Select(value => value.GetString()!).ToArray();
}
