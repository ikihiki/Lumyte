using System.Reflection;
using System.Text.Json;
using Lumyte.Benchmarks;
using Xunit;

namespace Lumyte.Core.Benchmark.Tests;

/// <summary>Checks the measured Core inventory without a redundant evaluation-scope category.</summary>
public sealed class CoreCoverageTests
{
    internal static readonly Assembly[] Assemblies =
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

    /// <summary>All thirteen products have real unique cases, with integrations absent from discovery.</summary>
    [Fact]
    public void CategoryInventoryIsCompleteAndUnambiguous()
    {
        Assert.Equal(13, Assemblies.Length);
        int total = 0;
        int screen = 0;
        foreach (Assembly assembly in Assemblies)
        {
            using var document = JsonDocument.Parse(BenchmarkEntry.CreateCatalog(assembly));
            JsonElement[] cases = document.RootElement.GetProperty("Cases").EnumerateArray().ToArray();
            Assert.NotEmpty(cases);
            Assert.Equal(cases.Length, cases.Select(benchmark => benchmark.GetProperty("ResultKey").GetString()).Distinct(StringComparer.Ordinal).Count());
            total += cases.Length;
            screen += cases.Count(benchmark => benchmark.GetProperty("Modes").EnumerateArray().Any(value => value.GetString() == "screen"));
        }

        Assert.Equal(680, total);
        Assert.Equal(674, screen);
    }

    /// <summary>DI and cross-product helper operations stay correctness-only, not hidden timing cases.</summary>
    [Fact]
    public void IntegrationHelpersAreNotBenchmarkCases()
    {
        foreach (Assembly assembly in Assemblies)
        {
            using var document = JsonDocument.Parse(BenchmarkEntry.CreateCatalog(assembly));
            foreach (JsonElement benchmark in document.RootElement.GetProperty("Cases").EnumerateArray())
            {
                string type = benchmark.GetProperty("Type").GetString()!;
                string method = benchmark.GetProperty("Method").GetString()!;
                Assert.DoesNotContain("Integration", type, StringComparison.Ordinal);
                Assert.DoesNotContain("DependencyInjection", type, StringComparison.Ordinal);
                Assert.DoesNotContain(method, new[] { "ProviderLifetimeAsync", "ComposeDefinition", "ResolveAndPrepare", "RegisterAndValidate", "SaveAndReload" });
            }
        }
    }
}
