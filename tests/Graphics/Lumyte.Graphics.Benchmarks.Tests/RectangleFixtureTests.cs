using Lumyte.Graphics.BenchmarkWorkloads;
using Xunit;

namespace Lumyte.Graphics.Benchmarks.Tests;

/// <summary>Input and recording invariants, with no elapsed-time assertions or GPU dependencies.</summary>
public sealed class RectangleFixtureTests
{
    /// <summary>Checks exact update counts, repeatable IDs, actual color transitions, and all partition sizes.</summary>
    /// <param name="update">The logical update profile.</param>
    /// <param name="expected">The exact number of actual changes.</param>
    [Theory]
    [InlineData("static", 0)]
    [InlineData("one", 1)]
    [InlineData("1%", 41)]
    [InlineData("10%", 410)]
    [InlineData("all", 4096)]
    public void TraceHasExactChanges(string update, int expected)
    {
        foreach (string locality in new[] { "contiguous", "dispersed" })
        {
            var trace = new RectangleTrace(update, locality);
            Assert.Equal(expected, trace.ChangedCount);
            Assert.Equal(expected, Enumerable.Range(0, 4096).Count(i => trace.IsWhite(i, 0) != trace.IsWhite(i, 1)));
            Assert.Equal(trace.InputId(0), trace.InputId(2));
            Assert.Equal(trace.InputId(1), new RectangleTrace(update, locality).InputId(1));
            Assert.Equal(expected == 0, trace.InputId(0) == trace.InputId(1));
            foreach (int granularity in new[] { 1, 16, 64, 256, 4096 })
            {
                using var scene = new RectangleFixture(trace, granularity, true);
                scene.Apply(1);
                Assert.Equal(trace.ChangedNodes(granularity).Length * granularity, scene.RerecordedCommands);
                scene.Apply(2);
                Assert.Equal(trace.ChangedNodes(granularity).Length * granularity, scene.RerecordedCommands);
            }
        }
    }

    /// <summary>Checks the Canvas static baseline keeps the completed immutable object.</summary>
    [Fact]
    public void CanvasStaticReusesRecording()
    {
        using var fixture = new RectangleFixture(new("static", "contiguous"), 64, false);
        object? first = fixture.Recording;
        fixture.Apply(1);
        Assert.Same(first, fixture.Recording);
        Assert.Equal(0, fixture.RerecordedCommands);
        Assert.Equal(4096, fixture.Recording!.Count);
    }

    /// <summary>Checks changed Canvas records all commands and same-state application is a genuine no-op.</summary>
    [Fact]
    public void CanvasChangesRebuildAndNoOpDoesNot()
    {
        using var fixture = new RectangleFixture(new("one", "dispersed"), 16, false);
        object? first = fixture.Recording;
        fixture.Apply(1);
        Assert.NotSame(first, fixture.Recording);
        Assert.Equal(4096, fixture.RerecordedCommands);
        object? second = fixture.Recording;
        fixture.Apply(1);
        Assert.Same(second, fixture.Recording);
        Assert.Equal(0, fixture.RerecordedCommands);
        Assert.Equal(0, fixture.ChangedNodes);
        Assert.Equal(0, fixture.ChangedElements);
    }

    /// <summary>Checks dispersed updates cannot contain duplicate elements and amplify more at coarse granularity.</summary>
    [Fact]
    public void DispersionIsUniqueAndSpansPartitions()
    {
        var contiguous = new RectangleTrace("1%", "contiguous");
        var dispersed = new RectangleTrace("1%", "dispersed");
        Assert.Equal(41, dispersed.ChangedNodes(1).Length);
        Assert.True(dispersed.ChangedNodes(64).Length > contiguous.ChangedNodes(64).Length);
        Assert.Throws<ArgumentOutOfRangeException>(() => dispersed.ChangedNodes(32));
        Assert.Throws<ArgumentException>(() => new RectangleTrace("noop", "contiguous"));
    }
}
