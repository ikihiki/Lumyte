using BenchmarkDotNet.Attributes;
using Lumyte.Core.Time;

namespace Lumyte.Animation.Benchmark;

/// <summary>Key lookup, segment rules, and tangent interpolation over bounded sample waves.</summary>
[BenchmarkCategory("Animation", "Curves", "Screening", "Comparison", "Diagnostic")]
public class CurveBenchmarks
{
    private AnimationKey<float>[] _keys = [];
    private AnimationHermiteKey<float>[] _hermiteKeys = [];
    private Duration[] _times = [];
    private AnimationCurve<float> _curve = null!;
    private AnimationHermiteCurve<float> _hermite = null!;

    /// <summary>Gets or sets the finite key count.</summary>
    [Params(8, 128)]
    public int KeyCount { get; set; }

    /// <summary>Gets or sets the outgoing segment rule.</summary>
    [Params("Linear", "Hold", "TemporalBezier", "SpatialBezier")]
    public string Segment { get; set; } = "Linear";

    /// <summary>Gets or sets the bounded sample distribution.</summary>
    [Params("Interior", "Boundaries")]
    public string InputProfile { get; set; } = "Interior";

    /// <summary>Copies keys and prepares sample times outside measurement.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _keys = new AnimationKey<float>[KeyCount];
        _hermiteKeys = new AnimationHermiteKey<float>[KeyCount];
        IAnimationTiming timing = AnimationTimings.CubicBezier(0.42, 0, 0.58, 1);
        for (int index = 0; index < KeyCount; index++)
        {
            var time = Duration.FromSeconds(index + 1);
            _keys[index] = new(time, index)
            {
                Hold = Segment == "Hold",
                Timing = Segment == "TemporalBezier" ? timing : null,
                Interpolator = Segment == "SpatialBezier"
                    ? AnimationInterpolators.CubicBezier(index + 0.25f, index + 0.75f, AnimationInterpolators.Float) : null,
            };
            _hermiteKeys[index] = new(time, index, 1, 1);
        }

        _curve = ConstructCurve();
        _hermite = ConstructHermite();
        _times = new Duration[64];
        for (int index = 0; index < _times.Length; index++)
        {
            int key = index * KeyCount / _times.Length;
            long ticks = _keys[key].Time.Ticks;
            _times[index] = InputProfile == "Interior"
                ? Duration.FromTicks(ticks + (TimeSpan.TicksPerSecond / 3))
                : (index % 4) switch
                {
                    0 => Duration.Zero,
                    1 => Duration.FromSeconds(KeyCount + 1),
                    2 => Duration.FromTicks(ticks),
                    _ => Duration.FromTicks(ticks - 1),
                };
        }
    }

    /// <summary>Samples copied keys, including endpoint holds and exact key boundaries.</summary>
    /// <returns>The sum of actual sampled values.</returns>
    [Benchmark]
    public float SampleCurve()
    {
        float sum = 0;
        foreach (Duration time in _times)
        {
            sum += _curve.Sample(time);
        }

        return sum;
    }

    /// <summary>Samples per-second Hermite tangents using the same key lookup distribution.</summary>
    /// <returns>The sum of actual sampled values.</returns>
    [Benchmark]
    public float SampleHermite()
    {
        float sum = 0;
        foreach (Duration time in _times)
        {
            sum += _hermite.Sample(time);
        }

        return sum;
    }

    /// <summary>Measures validation and key snapshot construction separately from sampling.</summary>
    /// <returns>The newly constructed curve.</returns>
    [Benchmark]
    public AnimationCurve<float> ConstructCurve() => new(Duration.FromSeconds(KeyCount + 1), _keys, AnimationInterpolators.Float);

    /// <summary>Measures tangent-key snapshot construction separately from sampling.</summary>
    /// <returns>The newly constructed tangent curve.</returns>
    [Benchmark]
    public AnimationHermiteCurve<float> ConstructHermite() => new(Duration.FromSeconds(KeyCount + 1), _hermiteKeys, AnimationHermiteInterpolators.Float);
}
