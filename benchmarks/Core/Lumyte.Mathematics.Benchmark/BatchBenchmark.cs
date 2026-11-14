using BenchmarkDotNet.Attributes;
using Lumyte.Mathematics.Geometry;

namespace Lumyte.Mathematics.Benchmark;

/// <summary>Seed-9417 SIMD batches retaining the original scalar distance oracle.</summary>
public class BatchBenchmark
{
    private float[] _x = [];
    private float[] _y = [];
    private float[] _output = [];
    private double[] _dx = [];
    private double[] _dy = [];
    private double[] _doubleOutput = [];

    /// <summary>Gets or sets SIMD tails and realistic batch lengths.</summary>
    [Params(0, 3, 31, 32, 33, 1024, 65536)]
    public int Length { get; set; }

    /// <summary>Builds deterministic nonidentical coordinates outside measurement.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(9417);
        _x = new float[Length];
        _y = new float[Length];
        _output = new float[Length];
        _dx = new double[Length];
        _dy = new double[Length];
        _doubleOutput = new double[Length];
        for (int i = 0; i < Length; i++)
        {
            _x[i] = (random.NextSingle() - 0.5f) * 100;
            _y[i] = (random.NextSingle() - 0.5f) * 100;
            _dx[i] = _x[i];
            _dy[i] = _y[i];
        }
    }

    /// <summary>Computes all single-precision distances into reused output.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-MATH-10")]
    public float[] Single()
    {
        Geometry2D.SquaredDistancesBcl(_x, _y, 1.25f, -3.75f, _output);
        return _output;
    }

    /// <summary>Computes all double-precision distances into reused output.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-MATH-10")]
    public double[] Double()
    {
        Geometry2D.SquaredDistancesBcl(_dx, _dy, 1.25, -3.75, _doubleOutput);
        return _doubleOutput;
    }

    /// <summary>Computes the original scalar formula over the identical single-precision batch.</summary>
    /// <returns>The complete reused scalar output.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-MATH-10")]
    public float[] ScalarSingle()
    {
        for (int i = 0; i < Length; i++)
        {
            float x = _x[i] - 1.25f;
            float y = _y[i] + 3.75f;
            _output[i] = (x * x) + (y * y);
        }

        return _output;
    }

    internal void Verify()
    {
        Single();
        Double();
        for (int i = 0; i < Length; i++)
        {
            float x = _x[i] - 1.25f;
            float y = _y[i] + 3.75f;
            double dx = _dx[i] - 1.25;
            double dy = _dy[i] + 3.75;
            if (Math.Abs(_output[i] - ((x * x) + (y * y))) > 0.002 || Math.Abs(_doubleOutput[i] - ((dx * dx) + (dy * dy))) > 1e-10)
            {
                throw new InvalidOperationException($"Distance oracle mismatch at {i}.");
            }
        }
    }
}
