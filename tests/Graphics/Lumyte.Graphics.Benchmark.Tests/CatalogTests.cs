using System.Reflection;
using System.Text.Json;
using Lumyte.Benchmarks;
using Xunit;

namespace Lumyte.Graphics.Benchmark.Tests;

/// <summary>Checks device-free metadata discovery and purpose-only classifications.</summary>
public sealed class CatalogTests
{
    /// <summary>Subjects use the namespace and class hierarchy instead of category labels.</summary>
    /// <param name="name">The executable assembly.</param>
    [Theory]
    [InlineData("Lumyte.Graphics.Drawing2D.Benchmark")]
    [InlineData("Lumyte.Graphics.RenderGraph.Benchmark")]
    [InlineData("Lumyte.Graphics.ImageEffects.Benchmark")]
    public void CpuCatalogUsesHierarchyAndPurposeOnly(string name)
    {
        var assembly = Assembly.Load(name);
        using var catalog = JsonDocument.Parse(BenchmarkEntry.CreateCatalog(assembly));
        JsonElement[] cases = catalog.RootElement.GetProperty("Cases").EnumerateArray().ToArray();
        Assert.NotEmpty(cases);
        foreach (JsonElement benchmark in cases)
        {
            string[] categories = benchmark.GetProperty("Categories").EnumerateArray().Select(value => value.GetString()!).ToArray();
            Assert.NotEmpty(categories);
            Assert.All(categories, category => Assert.Contains(category, new[] { "Screening", "Comparison", "Diagnostic" }));
            Assert.StartsWith(name, benchmark.GetProperty("Type").GetString()!, StringComparison.Ordinal);
        }
    }
}
