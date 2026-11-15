using BenchmarkDotNet.Attributes;
using Lumyte.Core.Time;
using static Lumyte.Animation.ComposeAnimation;

namespace Lumyte.Animation.Benchmark;

/// <summary>Measures finite update waves, including restart/rebase costs, without clock or event accumulation.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Animation", "Playback", "Screening", "Comparison", "Diagnostic")]
public class PlaybackBenchmarks
{
    private readonly AnimationChannel<float> _channel = AnimationChannel<float>.Create();
    private readonly AnimationOutput _output = new();
    private readonly List<AnimationEventOccurrence> _events = [];
    private ManualClock _clock = null!;
    private AnimationPlayback[] _playbacks = [];
    private AnimationPlayback _once = null!;
    private AnimationPlayback _pingPong = null!;

    /// <summary>Gets or sets the number of independent instances sharing an immutable definition.</summary>
    [Params(1, 32, 256)]
    public int Count { get; set; }

    /// <summary>Constructs all retained objects outside measurement.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _clock = new ManualClock();
        var source = new Tween<float>(0, 10, Duration.FromTicks(100), AnimationInterpolators.Float, AnimationEasing.Linear);
        ComposeAnimation.Definitions.Timeline definition = Timeline()[Track<float>(_channel, source)];
        AnimationTimeline timeline = definition.Build();
        _playbacks = new AnimationPlayback[Count];
        for (int i = 0; i < Count; i++)
        {
            _playbacks[i] = new AnimationPlayback(_clock, timeline, AnimationWrapMode.Loop);
            _playbacks[i].Play();
        }

        _once = new AnimationPlayback(_clock, timeline);

        // Ping-pong is composition, not an unsupported wrap-mode enum.
        _pingPong = new AnimationPlayback(_clock, Timeline()[Sequence()[definition, Reverse()[definition]]].Build(), AnimationWrapMode.Loop);
        _pingPong.Play();
    }

    /// <summary>Updates independent loop instances over a four-frame rebased wave.</summary>
    /// <returns>The sum of every instance's sampled values.</returns>
    [Benchmark]
    public double IndependentLoopWave()
    {
        foreach (AnimationPlayback playback in _playbacks)
        {
            playback.Seek(Duration.Zero);
        }

        double sum = 0;
        for (int frame = 0; frame < 4; frame++)
        {
            _clock.Advance(Duration.FromTicks(25));
            foreach (AnimationPlayback playback in _playbacks)
            {
                sum += Sample(playback);
            }
        }

        return sum;
    }

    /// <summary>Restarts once playback and samples through its completion boundary.</summary>
    /// <returns>Values plus the number of completion reports.</returns>
    [Benchmark]
    public double OnceCompletionWave()
    {
        _once.Stop();
        _once.Speed = 1;
        _once.Play();
        double sum = 0;
        for (int frame = 0; frame < 4; frame++)
        {
            _clock.Advance(Duration.FromTicks(25));
            sum += Sample(_once);
        }

        return sum;
    }

    /// <summary>Samples forward and reversed halves of a composed ping-pong cycle.</summary>
    /// <returns>The consumed sample sum.</returns>
    [Benchmark]
    public double PingPongWave()
    {
        _pingPong.Seek(Duration.Zero);
        double sum = 0;
        for (int frame = 0; frame < 8; frame++)
        {
            _clock.Advance(Duration.FromTicks(25));
            sum += Sample(_pingPong);
        }

        return sum;
    }

    /// <summary>Exercises pause, resume, seek, speed, cancellation and dormant update with bounded anchors.</summary>
    /// <returns>Values and completion reports from all evaluated phases.</returns>
    [Benchmark]
    public double PlaybackControlsCycle()
    {
        _once.Stop();
        _once.Speed = 1;
        _once.Play();
        _clock.Advance(Duration.FromTicks(25));
        _once.Pause();
        double sum = Sample(_once);
        _clock.Advance(Duration.FromTicks(10));
        _once.Resume();
        _once.Speed = 2;
        _once.Seek(Duration.FromTicks(50));
        _clock.Advance(Duration.FromTicks(25));
        sum += Sample(_once);
        _once.Cancel();
        sum += Sample(_once);
        _once.Stop();
        sum += Sample(_once);
        return sum;
    }

    private double Sample(AnimationPlayback playback)
    {
        _output.Clear();
        _events.Clear();
        bool completed = playback.Update(_output, _events);
        _output.TryGet(_channel, out float value);
        return value + _events.Count + (completed ? 1 : 0);
    }
}
