using Lumyte.Graphics.BenchmarkWorkloads;
using Lumyte.Graphics.Drawing2D;
using Xunit;

namespace Lumyte.Graphics.Benchmarks.Tests;

/// <summary>Deterministic burst/state and completion-owned history contracts; no wall-clock assertions.</summary>
public sealed class BurstSnapshotTests
{
    /// <summary>Checks fixed event boundaries and true full visible updates separated by static intervals.</summary>
    [Fact]
    public void BurstHasFixedTransitionsAndPartitionIndependentState()
    {
        var trace = new RectangleTrace("burst", "contiguous");
        Assert.Equal(trace.InputId(0), trace.InputId(29));
        Assert.NotEqual(trace.InputId(29), trace.InputId(30));
        Assert.Equal(trace.InputId(30), trace.InputId(149));
        Assert.Equal(trace.InputId(0), trace.InputId(150));
        Assert.Equal(trace.InputId(30), trace.InputId(270));
        Assert.Null(trace.BurstEventFrame(29));
        Assert.Equal(30, trace.BurstAnchor(29));
        Assert.Equal(150, trace.BurstAnchor(149));
        Assert.Null(new RectangleTrace("all", "contiguous").BurstAnchor(30));
        Assert.Equal(30, trace.BurstEventFrame(149));
        Assert.Equal(150, trace.BurstEventFrame(150));
        Assert.Equal(4096, Enumerable.Range(0, 4096).Count(i => trace.IsWhite(i, 29) != trace.IsWhite(i, 30)));
        Assert.Equal(trace.InputId(30), new RectangleTrace("burst", "dispersed").InputId(30));
        Assert.Throws<ArgumentOutOfRangeException>(() => trace.State(-1));
        foreach (int granularity in new[] { 1, 16, 64, 256, 4096 })
        {
            foreach (bool retained in new[] { false, true })
            {
                using var fixture = new RectangleFixture(trace, granularity, retained);
                fixture.Apply(29);
                Assert.Equal(0, fixture.ChangedElements);
                fixture.Apply(30);
                Assert.Equal(4096, fixture.ChangedElements);
                Assert.Equal(4096 / granularity, fixture.ChangedNodes);
                Assert.Equal(4096, fixture.RerecordedCommands);
                ulong revision = Revision(fixture);
                DrawList2D? recording = fixture.Recording;
                foreach (int frame in new[] { 30, 31, 149, 270, 30 })
                {
                    fixture.Apply(frame);
                    Assert.Equal(0, fixture.ChangedElements);
                    Assert.Equal(0, fixture.ChangedNodes);
                    Assert.Equal(0, fixture.RerecordedCommands);
                    Assert.Equal(revision, Revision(fixture));
                    Assert.Same(recording, fixture.Recording);
                }

                fixture.Apply(150);
                Assert.Equal(4096, fixture.ChangedElements);
                fixture.Apply(390);
                Assert.Equal(0, fixture.ChangedElements);
            }
        }
    }

    /// <summary>Checks bounded generations, no static duplicates, and eviction while GPU ownership is live.</summary>
    /// <param name="retained">Scene or Canvas route.</param>
    /// <param name="capacity">Past changed generation budget.</param>
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 3)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 3)]
    public void HistoryIsBoundedAndCompletionOwned(bool retained, int capacity)
    {
        var fixture = new RectangleFixture(new("burst", "contiguous"), 64, retained, snapshotHistory: capacity);
        fixture.Apply(30);
        Assert.Equal(Math.Min(1, capacity), fixture.History.Items.Count);
        fixture.Apply(31);
        Assert.Equal(Math.Min(1, capacity), fixture.History.Items.Count);
        RectangleGeneration? old = fixture.History.Items.FirstOrDefault();
        IDisposable? completion = old?.Hold();
        DrawingSnapshot2D? snapshot = old?.Snapshot;
        ulong? revision = snapshot?.Revision;
        DrawList2D? recording = old?.Recording;
        foreach (int frame in new[] { 150, 270, 390, 510 })
        {
            fixture.Apply(frame);
            Assert.InRange(fixture.History.Items.Count, 0, capacity);
            if (snapshot is not null)
            {
                Assert.Equal(revision, snapshot.Revision);
            }

            if (recording is not null)
            {
                Assert.Same(recording, old!.Recording);
                Assert.Equal(4096, recording.Count);
            }
        }

        fixture.Dispose();
        fixture.Dispose();
        if (snapshot is not null)
        {
            Assert.Equal(revision, snapshot.Revision);
        }

        completion?.Dispose();
        completion?.Dispose();
        if (snapshot is not null)
        {
            Assert.Throws<ObjectDisposedException>(() => snapshot.Revision);
        }

        if (old is not null)
        {
            Assert.Throws<ObjectDisposedException>(() => old.Hold());
        }

        Assert.Throws<ObjectDisposedException>(() => fixture.Apply(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RectangleSnapshotHistory(2));
    }

    /// <summary>Checks exact phase matching preserves periodic spikes and never averages away a burst regression.</summary>
    [Fact]
    public void PeriodicPilotComparesLikePhasesWithoutIdleDilution()
    {
        double[] values = Enumerable.Range(0, BurstPilot.WindowFrames)
            .Select(frame => frame % BurstPilot.CycleFrames is 29 or 149 ? 100.0 : 1.0).ToArray();
        Assert.True(BurstPilot.Stable(values));
        Assert.False(BurstPilot.Stable(values.Take(BurstPilot.WindowFrames - 1).ToArray()));
        values[29 + (3 * BurstPilot.CycleFrames)] = 106;
        Assert.False(BurstPilot.Stable(values));
        values[29 + (3 * BurstPilot.CycleFrames)] = 104;
        Assert.True(BurstPilot.Stable(values));
        values[0] = double.NaN;
        Assert.False(BurstPilot.Stable(values));
        values[0] = 0;
        Assert.False(BurstPilot.Stable(values));
    }

    private static ulong Revision(RectangleFixture fixture)
    {
        using DrawingSceneReadLease2D lease = fixture.Scene.Borrow();
        return lease.Revision;
    }
}
