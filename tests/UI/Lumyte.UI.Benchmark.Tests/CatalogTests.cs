using System.Reflection;
using System.Text.Json;
using Lumyte.Benchmarks;
using Xunit;

// Native log callbacks are process-global; hardware fixtures must not race during opt-in runs.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Lumyte.UI.Benchmark.Tests;

/// <summary>Checks device-free metadata discovery and purpose-only classifications.</summary>
public sealed class CatalogTests
{
    /// <summary>Subjects use the namespace and class hierarchy instead of category labels.</summary>
    /// <param name="name">The executable assembly.</param>
    [Theory]
    [InlineData("Lumyte.UI.Application.Browser.Benchmark")]
    [InlineData("Lumyte.UI.Application.Benchmark")]
    [InlineData("Lumyte.UI.Application.Desktop.Benchmark")]
    [InlineData("Lumyte.UI.Application.Navigation.Benchmark")]
    [InlineData("Lumyte.UI.Automation.Benchmark")]
    [InlineData("Lumyte.UI.Benchmark")]
    [InlineData("Lumyte.UI.Navigation.Benchmark")]
    [InlineData("Lumyte.UI.TextEditing.Benchmark")]
    [InlineData("Lumyte.UI.TextEditing.Parley.Benchmark")]
    [InlineData("Lumyte.UI.Windowing.Benchmark")]
    public void CatalogUsesHierarchyAndPurposeOnly(string name)
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
