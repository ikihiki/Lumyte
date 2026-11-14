using System.Numerics;
using BenchmarkDotNet.Attributes;
using Lumyte.Core.Time;

namespace Lumyte.Animation.Benchmark;

/// <summary>Representative scalar, vector, rotation, and typed-time tween interpolation.</summary>
[BenchmarkCategory("Animation", "Curves", "Screening", "Comparison", "Diagnostic")]
public class TweenBenchmarks
{
    private readonly Duration _duration = Duration.FromSeconds(1);
    private Duration[] _times = [];
    private Tween<float> _scalar = null!;
    private Tween<Vector3> _vector = null!;
    private Tween<Quaternion> _rotation = null!;
    private Tween<Duration> _time = null!;
    private IAnimationTiming _timing = null!;

    /// <summary>Gets or sets polynomial tween easing.</summary>
    [Params(AnimationEasing.Linear, AnimationEasing.EaseIn, AnimationEasing.EaseOut, AnimationEasing.EaseInOut)]
    public AnimationEasing Easing { get; set; }

    /// <summary>Prepares immutable sources and an endpoint-inclusive finite wave.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _scalar = ConstructTween();
        _vector = new(Vector3.Zero, new Vector3(2, 4, 6), _duration, AnimationInterpolators.Vector3, Easing);
        _rotation = new(Quaternion.Identity, Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2), _duration, AnimationInterpolators.Quaternion, Easing);
        _time = new(Duration.Zero, _duration, _duration, AnimationInterpolators.Duration, Easing);
        _timing = AnimationTimings.CubicBezier(0.42, 0, 0.58, 1);
        _times = new Duration[65];
        for (int index = 0; index < _times.Length; index++)
        {
            _times[index] = Duration.FromTicks(_duration.Ticks * index / 64);
        }
    }

    /// <summary>Samples a scalar tween over endpoints and interior times.</summary>
    /// <returns>The consumed scalar sum.</returns>
    [Benchmark]
    public float SampleScalar()
    {
        float sum = 0;
        foreach (Duration time in _times)
        {
            sum += _scalar.Sample(time);
        }

        return sum;
    }

    /// <summary>Samples vector interpolation.</summary>
    /// <returns>The consumed vector sum.</returns>
    [Benchmark]
    public Vector3 SampleVector()
    {
        Vector3 sum = Vector3.Zero;
        foreach (Duration time in _times)
        {
            sum += _vector.Sample(time);
        }

        return sum;
    }

    /// <summary>Samples normalized spherical rotation interpolation.</summary>
    /// <returns>The sum of quaternion components.</returns>
    [Benchmark]
    public float SampleRotation()
    {
        float sum = 0;
        foreach (Duration time in _times)
        {
            Quaternion value = _rotation.Sample(time);
            sum += value.X + value.Y + value.Z + value.W;
        }

        return sum;
    }

    /// <summary>Samples typed-duration interpolation without floating time accumulation.</summary>
    /// <returns>The sum of sampled ticks.</returns>
    [Benchmark]
    public long SampleDuration()
    {
        long sum = 0;
        foreach (Duration time in _times)
        {
            sum += _time.Sample(time).Ticks;
        }

        return sum;
    }

    /// <summary>Evaluates inverse-x temporal Bezier easing directly.</summary>
    /// <returns>The sum of transformed progress.</returns>
    [Benchmark]
    public double TransformTiming()
    {
        double sum = 0;
        foreach (Duration time in _times)
        {
            sum += _timing.Transform(time.Ticks / (double)_duration.Ticks);
        }

        return sum;
    }

    /// <summary>Constructs a validated tween separately from repeated sampling.</summary>
    /// <returns>The constructed source.</returns>
    [Benchmark]
    public Tween<float> ConstructTween() => new(0, 1, _duration, AnimationInterpolators.Float, Easing);
}
