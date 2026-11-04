using Lumyte.Graphics.BenchmarkWorkloads;
using Xunit;

namespace Lumyte.Graphics.Benchmarks.Tests;

/// <summary>Dedicated memory mode scope and ownership metadata, never byte or elapsed-time budgets.</summary>
public sealed class RetentionMemoryTests
{
    /// <summary>Checks checkpoint order and ownership while both public paths mutate and return.</summary>
    /// <param name="retained">Scene or Canvas path.</param>
    /// <param name="history">Past generation budget.</param>
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 3)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 3)]
    public void DetailedCheckpointsSeparateOwnedAndReleasedScopes(bool retained, int history)
    {
        RetentionMemorySample[] samples = ManagedRetentionProbe.MeasureDetailed(retained, 4096, history, cycles: 2);
        Assert.Equal(new[] { "baseline", "initialized", "cycle-return", "cycle-return", "after-dispose" }, samples.Select(sample => sample.Stage));
        Assert.False(samples[0].FixtureOwned);
        Assert.False(samples[^1].FixtureOwned);
        Assert.Null(samples[0].LogicalFrame);
        Assert.Null(samples[^1].LogicalFrame);
        Assert.Null(samples[^1].Cycle);
        Assert.Equal(0, samples[^1].RetainedGenerations);
        Assert.True(samples[1].FixtureOwned);
        Assert.Equal(0, samples[1].RetainedGenerations);
        for (int cycle = 1; cycle <= 2; cycle++)
        {
            RetentionMemorySample sample = samples[cycle + 1];
            Assert.True(sample.FixtureOwned);
            Assert.Equal(cycle, sample.Cycle);
            Assert.Equal(RectangleTrace.FirstBurstFrame + (((2 * cycle) - 1) * RectangleTrace.BurstPeriod), sample.LogicalFrame);
            Assert.Equal(Math.Min(history, 2 * cycle), sample.RetainedGenerations);
        }

        Assert.All(samples, sample =>
        {
            Assert.Null(sample.NativeAllocationBytes);
            Assert.Null(sample.GpuAllocationBytes);
        });
    }

    /// <summary>Checks legacy array shape and rejects invalid budgets before collecting.</summary>
    [Fact]
    public void LegacyShapeAndInputBoundsArePreserved()
    {
        Assert.Equal(3, ManagedRetentionProbe.Measure(false, 4096, 0, cycles: 1).Length);
        Assert.Throws<ArgumentOutOfRangeException>(() => ManagedRetentionProbe.MeasureDetailed(false, 4096, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ManagedRetentionProbe.MeasureDetailed(false, 4096, 0, 65));
        Assert.Throws<ArgumentOutOfRangeException>(() => ManagedRetentionProbe.MeasureDetailed(false, 32, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ManagedRetentionProbe.MeasureDetailed(false, 4096, 2));
    }

    /// <summary>Checks default all-profile sweep and explicit one-profile fresh-process selection.</summary>
    [Fact]
    public void MemoryOptionsSelectOneProfileOrDefaultSweep()
    {
        var defaults = RetentionMemoryOptions.Parse(["--retained-memory"]);
        Assert.Equal("all", defaults.Path);
        Assert.Null(defaults.SnapshotHistory);
        Assert.Equal(8, defaults.Cycles);
        var selected = RetentionMemoryOptions.Parse(["--memory-cycles", "64", "--retained-memory", "--snapshot-history", "3", "--memory-path", "scene"]);
        Assert.Equal(new RetentionMemoryOptions("scene", 3, 64), selected);
        Assert.Equal(new RetentionMemoryOptions("canvas", 0, 1), RetentionMemoryOptions.Parse(["--retained-memory", "--memory-path", "canvas", "--snapshot-history", "0", "--memory-cycles", "1"]));
        Assert.False(RetentionMemoryOptions.IsRequested(["--filter", "*Rectangle*"]));
    }

    /// <summary>Checks malformed memory invocations are caught rather than passed through to BenchmarkDotNet.</summary>
    /// <param name="command">Whitespace-separated malformed arguments.</param>
    [Theory]
    [InlineData("--retained-memory --memory-path")]
    [InlineData("--retained-memory --memory-path gpu")]
    [InlineData("--retained-memory --memory-path all")]
    [InlineData("--retained-memory --snapshot-history 2")]
    [InlineData("--retained-memory --snapshot-history 01")]
    [InlineData("--retained-memory --memory-cycles 0")]
    [InlineData("--retained-memory --memory-cycles 65")]
    [InlineData("--retained-memory --memory-cycles 1.5")]
    [InlineData("--retained-memory --memory-cycles -1")]
    [InlineData("--retained-memory --memory-cycles 99999999999999999")]
    [InlineData("--retained-memory --memory-cycles 1 --memory-cycles 2")]
    [InlineData("--retained-memory --retained-memory")]
    [InlineData("--retained-memory false")]
    [InlineData("--retained-memory --unknown x")]
    [InlineData("--memory-path scene")]
    [InlineData("--snapshot-history 3")]
    [InlineData("--memory-unknown 3")]
    [InlineData("--retained-memory-typo")]
    public void MemoryOptionErrorsNeverFallThrough(string command)
    {
        string[] args = command.Split(' ');
        Assert.True(RetentionMemoryOptions.IsRequested(args));
        Assert.Throws<ArgumentException>(() => RetentionMemoryOptions.Parse(args));
    }
}
