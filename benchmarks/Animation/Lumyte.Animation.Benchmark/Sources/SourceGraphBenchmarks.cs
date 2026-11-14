using BenchmarkDotNet.Attributes;
using Lumyte.Core.Time;
using static Lumyte.Animation.ComposeAnimation;

namespace Lumyte.Animation.Benchmark;

/// <summary>Compilation snapshots and bounded evaluation of shared Blend and TimeRemap graphs.</summary>
[BenchmarkCategory("Animation", "Sources", "Screening", "Comparison", "Diagnostic")]
public class SourceGraphBenchmarks
{
    private Definitions.Source<float> _definition = null!;
    private IAnimationSource<float> _compiled = null!;
    private IAnimationSource<float> _direct = null!;
    private Duration[] _times = [];

    /// <summary>Gets or sets the shared diamond graph depth.</summary>
    [Params(2, 8)]
    public int Depth { get; set; }

    /// <summary>Gets or sets whether sibling remaps use identical or distinct child-local times.</summary>
    [Params("SameLocalTime", "DifferentLocalTime")]
    public string InputProfile { get; set; } = "SameLocalTime";

    /// <summary>Prepares definitions and warms the compiled source's reusable evaluation workspace.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var duration = Duration.FromSeconds(1);
        var leaf = new Tween<float>(0, 100, duration, AnimationInterpolators.Float, AnimationEasing.Linear);
        var weight = new Tween<float>(0.5f, 0.5f, duration, AnimationInterpolators.Float, AnimationEasing.Linear);
        var forward = new Tween<Duration>(Duration.Zero, duration, duration, AnimationInterpolators.Duration, AnimationEasing.Linear);
        var reverse = new Tween<Duration>(duration, Duration.Zero, duration, AnimationInterpolators.Duration, AnimationEasing.Linear);
        Definitions.Sampled<float> sharedWeight = Sampled<float>(weight);
        Definitions.Sampled<Duration> forwardDefinition = Sampled<Duration>(forward);
        Definitions.Sampled<Duration> reverseDefinition = Sampled<Duration>(reverse);
        _definition = Sampled<float>(leaf);
        _direct = leaf;
        for (int index = 0; index < Depth; index++)
        {
            Definitions.Source<float> left = TimeRemap<float>(value: _definition, timeMap: forwardDefinition);
            Definitions.Source<float> right = TimeRemap<float>(value: _definition, timeMap: InputProfile == "SameLocalTime" ? forwardDefinition : reverseDefinition);
            _definition = Blend<float>(from: left, to: right, weight: sharedWeight, interpolator: AnimationInterpolators.Float);
            _direct = new AnimationBlend<float>(
                new AnimationTimeRemap<float>(_direct, forward),
                new AnimationTimeRemap<float>(_direct, InputProfile == "SameLocalTime" ? forward : reverse),
                weight,
                AnimationInterpolators.Float);
        }

        _compiled = Compile();
        _times = new Duration[32];
        for (int index = 0; index < _times.Length; index++)
        {
            _times[index] = Duration.FromTicks(duration.Ticks * (index + 1) / 33);
        }

        _ = SampleCompiled();
    }

    /// <summary>Compiles a fresh immutable snapshot from the retained shared definition graph.</summary>
    /// <returns>The compiled source.</returns>
    [Benchmark]
    public IAnimationSource<float> Compile() => _definition.Build();

    /// <summary>Samples a retained compiled graph, reusing cleared internal evaluation workspaces.</summary>
    /// <returns>The sum of actual graph outputs.</returns>
    [Benchmark]
    public float SampleCompiled() => Sample(_compiled);

    /// <summary>Samples the equivalent direct graph without compilation's sharing context.</summary>
    /// <returns>The sum of actual graph outputs.</returns>
    [Benchmark]
    public float SampleDirect() => Sample(_direct);

    private float Sample(IAnimationSource<float> source)
    {
        float sum = 0;
        foreach (Duration time in _times)
        {
            sum += source.Sample(time);
        }

        return sum;
    }
}
