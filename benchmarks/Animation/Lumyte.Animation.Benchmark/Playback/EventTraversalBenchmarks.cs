using BenchmarkDotNet.Attributes;
using Lumyte.Core.Time;

namespace Lumyte.Animation.Benchmark;

/// <summary>Traverses finite marker profiles in forward, reverse and wrapped playback.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Animation", "Events", "Screening", "Comparison", "Diagnostic")]
public class EventTraversalBenchmarks
{
    private readonly AnimationOutput _output = new();
    private readonly List<AnimationEventOccurrence> _events = [];
    private ManualClock _clock = null!;
    private AnimationPlayback _playback = null!;

    /// <summary>Gets or sets the marker count, including empty and singleton controls.</summary>
    [Params(0, 1, 16)]
    public int MarkerCount { get; set; }

    /// <summary>Gets or sets forward, reversed or two-cycle wrapped traversal.</summary>
    [Params("Forward", "Reverse", "Wrap")]
    public string Profile { get; set; } = "Forward";

    /// <summary>Creates immutable markers and reusable event storage outside measurement.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var builder = new AnimationTimelineBuilder();
        builder.SetDuration(Duration.FromTicks(100));
        for (int i = 0; i < MarkerCount; i++)
        {
            builder.AddEvent(Duration.FromTicks(10 + (i * 5)), "Marker");
        }

        AnimationTimeline timeline = builder.Build();
        if (Profile == "Reverse")
        {
            timeline = timeline.Reverse();
        }

        _clock = new ManualClock();
        _playback = new AnimationPlayback(_clock, timeline, Profile == "Wrap" ? AnimationWrapMode.Loop : AnimationWrapMode.Once);
        _events.Capacity = MarkerCount * 2;
    }

    /// <summary>Restarts and traverses one complete finite profile, consuming marker times and loop indices.</summary>
    /// <returns>The sum of occurrence positions and loop indices.</returns>
    [Benchmark]
    public long TraverseWave()
    {
        _playback.Stop();
        _playback.Play();
        _output.Clear();
        _events.Clear();
        _clock.Advance(Duration.FromTicks(Profile == "Wrap" ? 200 : 100));
        _playback.Update(_output, _events);
        long sum = _events.Count;
        foreach (AnimationEventOccurrence occurrence in _events)
        {
            sum += occurrence.UpdateOffset.Ticks + occurrence.LoopIndex;
        }

        return sum;
    }
}
