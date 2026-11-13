using System.Diagnostics;
using System.Runtime.CompilerServices;
using Lumyte.Mathematics.Geometry;

namespace Lumyte.Mathematics.Benchmark;

internal static class BatchBenchmark
{
    private static double _sink;

    internal static void Run()
    {
#if DEBUG
        throw new InvalidOperationException("Use Release.");
#else
        Console.WriteLine($"Runtime={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; OS={System.Runtime.InteropServices.RuntimeInformation.OSDescription}; Architecture={System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}; CPUs={Environment.ProcessorCount}; Vector128={System.Runtime.Intrinsics.Vector128.IsHardwareAccelerated}; Vector256={System.Runtime.Intrinsics.Vector256.IsHardwareAccelerated}");
        Console.WriteLine($"TieredCompilation={Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "default"}; HWIntrinsic={Environment.GetEnvironmentVariable("DOTNET_EnableHWIntrinsic") ?? "default"}");
        Console.WriteLine("type,length,iteration,variant,ns/point,bytes/batch,checksum");
        foreach (int length in new[] { 3, 32, 1024, 65536 })
        {
            Single(length);
            Double(length);
        }

        GC.KeepAlive(_sink);
#endif
    }

    private static void Single(int length)
    {
        var random = new Random(9417);
        float[] x = new float[length];
        float[] y = new float[length];
        float[] output = new float[length];
        float[] expected = new float[length];
        for (int i = 0; i < length; i++)
        {
            x[i] = (float)((random.NextDouble() * 100) - 50);
            y[i] = (float)((random.NextDouble() * 100) - 50);
        }

        Reference(x, y, 1.25f, -3.75f, expected);
        Geometry2D.SquaredDistancesBcl(x, y, 1.25f, -3.75f, output);
        if (!output.AsSpan().SequenceEqual(expected))
        {
            throw new InvalidOperationException("Scalar oracle mismatch.");
        }

        int operations = Math.Max(512, 2097152 / length);

        // Same arrays, checks, arithmetic, store and consumption boundaries. Setup excluded.
        for (int warmup = 0; warmup < 6; warmup++)
        {
            Execute(false);
            Execute(true);
        }

        for (int pair = 0; pair < 10; pair++)
        {
            Measure(pair, pair % 2 == 0);
            Measure(pair, pair % 2 != 0);
        }

        void Execute(bool optimized)
        {
            for (int operation = 0; operation < operations; operation++)
            {
                if (optimized)
                {
                    Geometry2D.SquaredDistancesBcl(x, y, 1.25f, -3.75f, output);
                }
                else
                {
                    Reference(x, y, 1.25f, -3.75f, output);
                }
            }
        }

        void Measure(int pair, bool optimized)
        {
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            Execute(optimized);
            long elapsed = Stopwatch.GetTimestamp() - start;
            long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
            double checksum = 0;
            foreach (float value in output)
            {
                checksum += value;
            }

            _sink = checksum;
            Console.WriteLine($"float,{length},{pair},{(optimized ? "simd" : "scalar")},{elapsed * 1e9 / Stopwatch.Frequency / operations / length:F5},{bytes / (double)operations:F3},{checksum:R}");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Reference(ReadOnlySpan<float> x, ReadOnlySpan<float> y, float pointX, float pointY, Span<float> destination)
    {
        if (x.Length != y.Length || destination.Length < x.Length)
        {
            throw new ArgumentException("Coordinate lengths must match and destination must not be shorter.");
        }

        ReadOnlySpan<float> output = destination[..x.Length];
        if ((x.Overlaps(output, out int xOffset) && xOffset != 0)
            || (y.Overlaps(output, out int yOffset) && yOffset != 0))
        {
            throw new ArgumentException("Destination must not have shifted overlap with either coordinate span.");
        }

        for (int i = 0; i < x.Length; i++)
        {
            float dx = x[i] - pointX;
            float dy = y[i] - pointY;
            destination[i] = (dx * dx) + (dy * dy);
        }
    }

    private static void Double(int length)
    {
        var random = new Random(9417);
        double[] x = new double[length];
        double[] y = new double[length];
        double[] output = new double[length];
        double[] expected = new double[length];
        for (int i = 0; i < length; i++)
        {
            x[i] = (double)((random.NextDouble() * 100) - 50);
            y[i] = (double)((random.NextDouble() * 100) - 50);
        }

        Reference(x, y, 1.25d, -3.75d, expected);
        Geometry2D.SquaredDistancesBcl(x, y, 1.25d, -3.75d, output);
        if (!output.AsSpan().SequenceEqual(expected))
        {
            throw new InvalidOperationException("Scalar oracle mismatch.");
        }

        int operations = Math.Max(512, 2097152 / length);

        // Same arrays, checks, arithmetic, store and consumption boundaries. Setup excluded.
        for (int warmup = 0; warmup < 6; warmup++)
        {
            Execute(false);
            Execute(true);
        }

        for (int pair = 0; pair < 10; pair++)
        {
            Measure(pair, pair % 2 == 0);
            Measure(pair, pair % 2 != 0);
        }

        void Execute(bool optimized)
        {
            for (int operation = 0; operation < operations; operation++)
            {
                if (optimized)
                {
                    Geometry2D.SquaredDistancesBcl(x, y, 1.25d, -3.75d, output);
                }
                else
                {
                    Reference(x, y, 1.25d, -3.75d, output);
                }
            }
        }

        void Measure(int pair, bool optimized)
        {
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            Execute(optimized);
            long elapsed = Stopwatch.GetTimestamp() - start;
            long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
            double checksum = 0;
            foreach (double value in output)
            {
                checksum += value;
            }

            _sink = checksum;
            Console.WriteLine($"double,{length},{pair},{(optimized ? "simd" : "scalar")},{elapsed * 1e9 / Stopwatch.Frequency / operations / length:F5},{bytes / (double)operations:F3},{checksum:R}");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Reference(ReadOnlySpan<double> x, ReadOnlySpan<double> y, double pointX, double pointY, Span<double> destination)
    {
        if (x.Length != y.Length || destination.Length < x.Length)
        {
            throw new ArgumentException("Coordinate lengths must match and destination must not be shorter.");
        }

        ReadOnlySpan<double> output = destination[..x.Length];
        if ((x.Overlaps(output, out int xOffset) && xOffset != 0)
            || (y.Overlaps(output, out int yOffset) && yOffset != 0))
        {
            throw new ArgumentException("Destination must not have shifted overlap with either coordinate span.");
        }

        for (int i = 0; i < x.Length; i++)
        {
            double dx = x[i] - pointX;
            double dy = y[i] - pointY;
            destination[i] = (dx * dx) + (dy * dy);
        }
    }
}
