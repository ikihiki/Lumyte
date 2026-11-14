using System.Reflection;
using System.Text.Json;
using Lumyte.Benchmarks;
using Xunit;

namespace Lumyte.Audio.Benchmark.Tests;

/// <summary>Checks semantic selection across the Audio product executables.</summary>
public sealed class CatalogTests
{
    /// <summary>Every executable exposes Audio ownership and explicit measurement purposes.</summary>
    /// <param name="name">The benchmark assembly.</param>
    [Theory]
    [InlineData("Lumyte.Audio.Execution.Benchmark")]
    [InlineData("Lumyte.Audio.OpenAL.Benchmark")]
    [InlineData("Lumyte.Resources.Audio.Benchmark")]
    public void CatalogDeclaresPurposeAndAudioOwnership(string name)
    {
        var assembly = Assembly.Load(name);
        using var catalog = JsonDocument.Parse(BenchmarkEntry.CreateCatalog(assembly));
        Assert.Equal(new[] { "screen", "compare", "diagnose" }, catalog.RootElement.GetProperty("Modes").EnumerateArray().Select(value => value.GetString()!).ToArray());
        JsonElement[] cases = catalog.RootElement.GetProperty("Cases").EnumerateArray().ToArray();
        Assert.NotEmpty(cases);
        foreach (JsonElement benchmark in cases)
        {
            string[] categories = benchmark.GetProperty("Categories").EnumerateArray().Select(value => value.GetString()!).ToArray();
            string[] modes = benchmark.GetProperty("Modes").EnumerateArray().Select(value => value.GetString()!).ToArray();
            Assert.Contains("Audio", categories);
            Assert.DoesNotContain(categories, category => category.StartsWith("AUDIO-", StringComparison.Ordinal) || category.StartsWith("CORE-", StringComparison.Ordinal));
            foreach ((string purpose, string mode) in new[] { ("Screening", "screen"), ("Comparison", "compare"), ("Diagnostic", "diagnose") })
            {
                Assert.Equal(categories.Contains(purpose, StringComparer.Ordinal), modes.Contains(mode, StringComparer.Ordinal));
            }
        }
    }

    /// <summary>Listing does not execute a replacement correctness mode.</summary>
    [Fact]
    public void NoArgumentsListAndRemovedModeIsRejected()
    {
        BenchmarkEntry.EntryOptions options = BenchmarkEntry.Parse([]);
        Assert.True(options.Catalog);
        Assert.Null(options.Mode);
        Assert.Throws<ArgumentException>(() => BenchmarkEntry.Parse(["--mode", "verify"]));
    }
}
