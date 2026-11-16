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
    [InlineData("Lumyte.Graphics.Validation.Benchmark")]
    [InlineData("Lumyte.Graphics.Benchmark")]
    [InlineData("Lumyte.Graphics.Drawing.Benchmark")]
    [InlineData("Lumyte.Graphics.Drawing2D.Benchmark")]
    [InlineData("Lumyte.Graphics.Generators.Benchmark")]
    [InlineData("Lumyte.Graphics.Hosting.Benchmark")]
    [InlineData("Lumyte.Graphics.ImageEffects.Benchmark")]
    [InlineData("Lumyte.Graphics.RenderGraph.Benchmark")]
    [InlineData("Lumyte.Graphics.Shaders.Benchmark")]
    [InlineData("Lumyte.Graphics.Text.Benchmark")]
    [InlineData("Lumyte.Graphics.Text.Parley.Benchmark")]
    [InlineData("Lumyte.Resources.Graphics.Benchmark")]
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

    /// <summary>Both map families are discoverable without constructing a device or fixture.</summary>
    /// <param name="assemblyName">Existing executable assembly.</param>
    /// <param name="family">Expected benchmark family.</param>
    /// <param name="methods">Expanded parameter case count.</param>
    [Theory]
    [InlineData("Lumyte.Graphics.Drawing2D.Benchmark", "MapCpuBenchmarks", 5)]
    [InlineData("Lumyte.Graphics.Drawing2D.Benchmark", "MapStreamingBenchmarks", 1)]
    [InlineData("Lumyte.Graphics.Benchmark", "MapFramesBenchmarks", 8)]
    public void MapsCatalogIsDeviceFree(string assemblyName, string family, int methods)
    {
        using var catalog = JsonDocument.Parse(BenchmarkEntry.CreateCatalog(Assembly.Load(assemblyName)));
        JsonElement[] cases = catalog.RootElement.GetProperty("Cases").EnumerateArray()
            .Where(item => item.GetProperty("Type").GetString()!.EndsWith(family, StringComparison.Ordinal)).ToArray();
        Assert.Equal(methods, cases.Length);
    }
}
