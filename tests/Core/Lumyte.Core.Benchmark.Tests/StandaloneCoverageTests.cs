using System.Reflection;
using System.Text.Json;
using Lumyte.Benchmarks;
using Xunit;

namespace Lumyte.Core.Benchmark.Tests;

/// <summary>Guards the independent category boundary using compiled benchmark metadata.</summary>
public sealed class StandaloneCoverageTests
{
    private static readonly Assembly[] _assemblies =
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

    /// <summary>Every independently owned product has actual, mode-eligible parameterized workloads.</summary>
    [Fact]
    public void EveryIndependentProjectHasRunnableStandaloneCases()
    {
        Assert.Equal(13, _assemblies.Length);
        foreach (Assembly assembly in _assemblies)
        {
            using var document = JsonDocument.Parse(BenchmarkEntry.CreateCatalog(assembly));
            JsonElement[] cases = document.RootElement.GetProperty("Cases").EnumerateArray().ToArray();
            JsonElement[] standalone = cases.Where(benchmark => Categories(benchmark).Contains("Standalone", StringComparer.Ordinal)).ToArray();
            Assert.NotEmpty(standalone);
            Assert.Contains(standalone, benchmark => Strings(benchmark, "Modes").Contains("screen", StringComparer.Ordinal));
            Assert.Equal(cases.Length, cases.Select(Identity).Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(cases.Length, cases.Select(benchmark => benchmark.GetProperty("ResultKey").GetString()).Distinct(StringComparer.Ordinal).Count());
            Assert.All(cases, benchmark => Assert.Contains("|", benchmark.GetProperty("ResultKey").GetString(), StringComparison.Ordinal));
            foreach (JsonElement benchmark in standalone)
            {
                string[] categories = Categories(benchmark);
                Assert.DoesNotContain("DependencyInjection", categories);
                Assert.DoesNotContain("Integration", categories);
                Assert.Contains(categories, category => category is "SteadyState" or "Construction" or "Lifetime" or "CompileTime" or "FileIO" or "Contention" or "DiagnosticPath");
            }
        }
    }

    /// <summary>Explicit adapters remain discoverable but cannot masquerade as standalone subjects.</summary>
    [Fact]
    public void IntegrationAndDependencyInjectionAreExplicitlyExcluded()
    {
        var excluded = new HashSet<string>(StringComparer.Ordinal);
        foreach (Assembly assembly in _assemblies)
        {
            using var document = JsonDocument.Parse(BenchmarkEntry.CreateCatalog(assembly));
            foreach (JsonElement benchmark in document.RootElement.GetProperty("Cases").EnumerateArray())
            {
                string type = benchmark.GetProperty("Type").GetString()!;
                string method = benchmark.GetProperty("Method").GetString()!;
                bool adapter = type == "Lumyte.Execution.Benchmark.DependencyInjectionBenchmarks"
                    || type == "Lumyte.Resources.Benchmark.IntegrationBenchmarks"
                    || type == "Lumyte.Resources.Benchmark.StreamingBenchmarks"
                    || (type == "Lumyte.Memory.Benchmark.MemoryBenchmarks" && method == "ProviderLifetimeAsync")
                    || (type == "Lumyte.StateMachines.Benchmark.StateMachinesBenchmarks" && method == "ComposeDefinition");
                if (!adapter)
                {
                    continue;
                }

                string[] categories = Categories(benchmark);
                Assert.DoesNotContain("Standalone", categories);
                Assert.Contains(categories, category => category is "DependencyInjection" or "Integration");
                excluded.Add(type + "." + method);
            }
        }

        Assert.Contains("Lumyte.Execution.Benchmark.DependencyInjectionBenchmarks.ResolveAndPrepare", excluded);
        Assert.Contains("Lumyte.Memory.Benchmark.MemoryBenchmarks.ProviderLifetimeAsync", excluded);
        Assert.Contains("Lumyte.Resources.Benchmark.StreamingBenchmarks.ReadAllAsync", excluded);
        Assert.Contains("Lumyte.StateMachines.Benchmark.StateMachinesBenchmarks.ComposeDefinition", excluded);
    }

    /// <summary>Compile-time generation remains independent but is never labelled per-frame work.</summary>
    [Fact]
    public void GeneratorsAreSeparatedFromSteadyFrameCosts()
    {
        foreach (Assembly assembly in _assemblies.Where(assembly => assembly.GetName().Name!.Contains(".Generators.", StringComparison.Ordinal)))
        {
            using var document = JsonDocument.Parse(BenchmarkEntry.CreateCatalog(assembly));
            foreach (JsonElement benchmark in document.RootElement.GetProperty("Cases").EnumerateArray())
            {
                Assert.Contains("CompileTime", Categories(benchmark));
                Assert.DoesNotContain("SteadyState", Categories(benchmark));
            }
        }
    }

    private static string Identity(JsonElement benchmark) => benchmark.GetProperty("Type").GetString() + "."
        + benchmark.GetProperty("Method").GetString() + ":" + benchmark.GetProperty("Parameters").GetRawText();

    private static string[] Categories(JsonElement benchmark) => Strings(benchmark, "Categories");

    private static string[] Strings(JsonElement benchmark, string property) => benchmark.GetProperty(property).EnumerateArray().Select(value => value.GetString()!).ToArray();
}
