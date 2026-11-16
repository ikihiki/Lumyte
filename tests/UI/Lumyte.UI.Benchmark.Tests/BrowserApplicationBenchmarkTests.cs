using Lumyte.UI.Application.Browser.Benchmark;
using Xunit;

namespace Lumyte.UI.Benchmark.Tests;

/// <summary>Functional-only browser integration; missing prerequisites are explicit real skips.</summary>
public sealed class BrowserApplicationBenchmarkTests
{
    /// <summary>Verifies distinct real completed frames and untimed analytic GPU readback.</summary>
    /// <param name="profile">Actual mutation coverage and control count.</param>
    /// <returns>Complete private-browser lifetime.</returns>
    [BrowserHardwareTheory]
    [InlineData("Local100")]
    [InlineData("Full1000")]
    public async Task ActualApplicationFramesAndAnalyticPixelsAsync(string profile)
    {
        await using var session = new BrowserSession();
        await session.StartAsync(profile);
        Assert.Contains("adapter", session.AdapterAudit, StringComparison.Ordinal);
        string? artifacts = Environment.GetEnvironmentVariable("LUMYTE_UI_BENCHMARK_PROOF");
        if (artifacts is not null)
        {
            Directory.CreateDirectory(artifacts);
            await File.WriteAllTextAsync(Path.Combine(artifacts, profile + "-adapter.json"), session.AdapterAudit);
        }

        long first = await session.AdvanceAsync();
        long second = await session.AdvanceAsync();
        Assert.True(second > first);
        int[][] pixels = await session.ReadOracleAsync();
        Assert.Equal([0, 255, 0, 255], pixels[0]);
        Assert.Equal(profile == "Full1000" ? [0, 255, 0, 255] : [255, 0, 0, 255], pixels[1]);
        Assert.Equal([255, 255, 255, 255], pixels[2]);
        Assert.Equal([255, 255, 255, 255], pixels[3]);
        if (artifacts is not null)
        {
            await File.WriteAllBytesAsync(Path.Combine(artifacts, profile + "-oracle.png"), await session.CaptureAsync());
        }
    }

    private sealed class BrowserHardwareTheoryAttribute : TheoryAttribute
    {
        public BrowserHardwareTheoryAttribute()
        {
            Skip = BrowserSession.GetUnavailableReason();
        }
    }
}
