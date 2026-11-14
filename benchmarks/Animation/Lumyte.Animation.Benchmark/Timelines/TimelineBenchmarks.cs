using BenchmarkDotNet.Attributes;
using Lumyte.Core.Time;
using static Lumyte.Animation.ComposeAnimation;

namespace Lumyte.Animation.Benchmark;

/// <summary>Separates editable composition construction from retained timeline sampling.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Animation", "Timeline", "Screening", "Comparison", "Diagnostic")]
public class TimelineBenchmarks
{
    private readonly AnimationChannel<float> _channel = AnimationChannel<float>.Create();
    private readonly AnimationOutput _output = new();
    private readonly List<AnimationEventOccurrence> _events = [];
    private AnimationPlayback _playback = null!;
    private ManualClock _clock = null!;

    /// <summary>Creates the immutable composition and playback outside sampling.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _clock = new ManualClock();
        _playback = new AnimationPlayback(_clock, BuildComposition(), AnimationWrapMode.Loop);
        _playback.Play();
    }

    /// <summary>Builds sequence, parallel, delay, compact repeat, reverse and marker nodes.</summary>
    /// <returns>The immutable timeline.</returns>
    [Benchmark]
    public AnimationTimeline BuildComposition()
    {
        var source = new Tween<float>(0, 10, Duration.FromTicks(100), AnimationInterpolators.Float, AnimationEasing.Linear);
        ComposeAnimation.Definitions.Timeline forward = Timeline()[Track<float>(_channel, source)];
        return Timeline()[Repeat(4)[Sequence()[Delay(Duration.FromTicks(20)), Parallel()[forward, Marker("Begin")], Reverse()[forward], Marker("End")]]].Build();
    }

    /// <summary>Samples an existing composition in bounded, seek-rebased waves.</summary>
    /// <returns>The sum of sampled values and emitted marker counts.</returns>
    [Benchmark]
    public double SampleCompositionWave()
    {
        _playback.Seek(Duration.Zero);
        double sum = 0;
        for (int i = 0; i < 16; i++)
        {
            _clock.Advance(Duration.FromTicks(55));
            _output.Clear();
            _events.Clear();
            _playback.Update(_output, _events);
            _output.TryGet(_channel, out float value);
            sum += value + _events.Count;
        }

        return sum;
    }
}
