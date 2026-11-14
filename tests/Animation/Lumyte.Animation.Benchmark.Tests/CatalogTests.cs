using System.Reflection;
using System.Text.Json;
using Lumyte.Benchmarks;
using Xunit;

namespace Lumyte.Animation.Benchmark.Tests;

/// <summary>Checks metadata-driven selection without timing or filesystem classification.</summary>
public sealed class CatalogTests
{
    /// <summary>Animation workloads expose semantic groups and only explicitly declared measurement purposes.</summary>
    [Fact]
    public void CasesDeclareAnimationAndMeasurementPurposes()
    {
        var assembly = Assembly.Load("Lumyte.Animation.Benchmark");
        using var catalog = JsonDocument.Parse(BenchmarkEntry.CreateCatalog(assembly));
        string[] supported = catalog.RootElement.GetProperty("Modes").EnumerateArray().Select(value => value.GetString()!).ToArray();
        Assert.Equal(new[] { "screen", "compare", "diagnose" }, supported);
        JsonElement[] cases = catalog.RootElement.GetProperty("Cases").EnumerateArray().ToArray();
        Assert.NotEmpty(cases);
        foreach (JsonElement benchmark in cases)
        {
            string[] categories = benchmark.GetProperty("Categories").EnumerateArray().Select(value => value.GetString()!).ToArray();
            string[] modes = benchmark.GetProperty("Modes").EnumerateArray().Select(value => value.GetString()!).ToArray();
            Assert.Contains("Animation", categories);
            Assert.DoesNotContain(categories, category => category.StartsWith("ANIM-", StringComparison.Ordinal) || category.StartsWith("CORE-", StringComparison.Ordinal));
            foreach ((string purpose, string mode) in new[] { ("Screening", "screen"), ("Comparison", "compare"), ("Diagnostic", "diagnose") })
            {
                Assert.Equal(categories.Contains(purpose, StringComparer.Ordinal), modes.Contains(mode, StringComparer.Ordinal));
            }
        }
    }

    /// <summary>The removed verification mode cannot be reintroduced by an executable default.</summary>
    [Fact]
    public void DefaultIsUntimedCatalogAndVerifyIsRejected()
    {
        BenchmarkEntry.EntryOptions options = BenchmarkEntry.Parse([]);
        Assert.True(options.Catalog);
        Assert.Null(options.Mode);
        Assert.Throws<ArgumentException>(() => BenchmarkEntry.Parse(["--mode", "verify"]));
    }
}
