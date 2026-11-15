using System.Numerics;
using BenchmarkDotNet.Attributes;
using Lumyte.Core.Time;

namespace Lumyte.Animation.Benchmark;

/// <summary>Measures public playback merging into retained heterogeneous typed slots.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Animation", "Output", "Screening", "Comparison", "Diagnostic")]
public class OutputBenchmarks
{
    private readonly AnimationChannel<float> _scalar = AnimationChannel<float>.Create();
    private readonly AnimationChannel<Vector2> _vector = AnimationChannel<Vector2>.Create();
    private readonly AnimationChannel<Duration> _duration = AnimationChannel<Duration>.Create();
    private readonly AnimationOutput _output = new();
    private readonly List<AnimationEventOccurrence> _events = [];
    private AnimationPlayback _first = null!;
    private AnimationPlayback _second = null!;

    /// <summary>Creates paused playbacks so values are stable and no timing segments accumulate.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var clock = new ManualClock();
        var first = new AnimationTimelineBuilder();
        first.Add(Duration.Zero, new Tween<float>(0, 10, Duration.FromTicks(100), AnimationInterpolators.Float, AnimationEasing.Linear), _scalar);
        first.Add(Duration.Zero, new Tween<Vector2>(Vector2.Zero, new Vector2(10, 20), Duration.FromTicks(100), AnimationInterpolators.Vector2, AnimationEasing.Linear), _vector);
        var second = new AnimationTimelineBuilder();
        second.Add(Duration.Zero, new Tween<float>(100, 200, Duration.FromTicks(100), AnimationInterpolators.Float, AnimationEasing.Linear), _scalar);
        second.Add(Duration.Zero, new Tween<Duration>(Duration.Zero, Duration.FromTicks(100), Duration.FromTicks(100), AnimationInterpolators.Duration, AnimationEasing.Linear), _duration);
        _first = new AnimationPlayback(clock, first.Build());
        _second = new AnimationPlayback(clock, second.Build());
        _first.Play();
        _second.Play();
        clock.Advance(Duration.FromTicks(50));
        _first.Pause();
        _second.Pause();
        MergeAndClear();
    }

    /// <summary>Merges two evaluations, overwrites a shared scalar, reads three types and clears reusable slots.</summary>
    /// <returns>The scalar, vector and duration checksum.</returns>
    [Benchmark]
    public double MergeAndClear()
    {
        _output.Clear();
        _events.Clear();
        _first.Update(_output, _events);
        _second.Update(_output, _events);
        _output.TryGet(_scalar, out float scalar);
        _output.TryGet(_vector, out Vector2 vector);
        _output.TryGet(_duration, out Duration duration);
        double sum = scalar + vector.X + vector.Y + duration.Ticks;
        _output.Clear();
        return sum;
    }
}
