using Lumyte.Resources.Benchmark.Management;
using Lumyte.Settings;
using Lumyte.Settings.Benchmark.PersistedSettings;
using Lumyte.Signals.Benchmark.Effects;
using Xunit;

namespace Lumyte.Core.Benchmark.Tests;

/// <summary>Checks benchmark semantics without timing or allocation thresholds.</summary>
public sealed class SignalsResourcesSettingsTests
{
    /// <summary>Runs deterministic checks from all three target executables.</summary>
    [Fact]
    public void TargetOracles()
    {
        Lumyte.Signals.Benchmark.BenchmarkChecks.Verify();
        Lumyte.Resources.Benchmark.BenchmarkChecks.Verify();
        Lumyte.Settings.Benchmark.BenchmarkChecks.Verify();
    }

    /// <summary>Confirms empty scope ownership and same-revision computed recovery.</summary>
    [Fact]
    public void SignalBoundaryResults()
    {
        var benchmark = new LifetimeBenchmarks { Observers = 0 };
        Assert.Equal(0, benchmark.ScopeLifetime());
        Assert.Equal(42, benchmark.FailureAndRetry());
    }

    /// <summary>Checks validation and conflict remain distinguishable from successful commits.</summary>
    [Fact]
    public void SettingsBoundaryResults()
    {
        var benchmark = new SettingsBenchmarks { Items = 0 };
        benchmark.Setup();
        try
        {
            Assert.Equal(0, benchmark.Snapshot());
            Assert.Equal(SettingsSaveStatus.ValidationFailed, benchmark.Invalid());
            Assert.Equal(SettingsSaveStatus.Saved, benchmark.Save());
            Assert.Equal(SettingsSaveStatus.Conflict, benchmark.Conflict());
            Assert.Equal(SettingsSaveStatus.RecoveryRequired, benchmark.CorruptionProtection());
        }
        finally
        {
            benchmark.Cleanup();
        }
    }

    /// <summary>Checks the one-node graph releases one actual output, not an empty trim.</summary>
    /// <returns>Completion of graph creation and reclamation.</returns>
    [Fact]
    public async Task ResourceBoundaryResultsAsync()
    {
        var benchmark = new ResourcesBenchmarks { Depth = 1 };
        benchmark.Setup();
        try
        {
            Assert.Equal(1, benchmark.CacheHit());
            Assert.Equal(1, await benchmark.BuildAndTrimAsync());
        }
        finally
        {
            benchmark.Cleanup();
        }
    }
}
