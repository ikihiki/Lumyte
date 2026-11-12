using System.Numerics;
using System.Runtime.CompilerServices;
using Lumyte.Mathematics.Geometry;

namespace Lumyte.Mathematics.Benchmarks;

internal static class Program
{
    private const int Operations = 500000;
    private const int Repetitions = 10;
    private static Vector4 _sink;

    private static void Main(string[] args)
    {
        if (args.Contains("--batch"))
        {
            BatchBenchmark.Run();
            return;
        }

#if DEBUG
        throw new InvalidOperationException("Run this benchmark in Release configuration.");
#else
        Console.WriteLine($"Runtime={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; OS={System.Runtime.InteropServices.RuntimeInformation.OSDescription}; Architecture={System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}; CPUs={Environment.ProcessorCount}");
        Console.WriteLine($"TieredCompilation={Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "default"}; Operations={Operations}; Repetitions={Repetitions}; timestampFrequency={System.Diagnostics.Stopwatch.Frequency}");
        var first = new Vector2[256];
        var last = new Vector2[256];
        var transforms = new Matrix3x2[256];
        var random = new Random(9417);
        for (int i = 0; i < first.Length; i++)
        {
            first[i] = new(Next(), Next());
            last[i] = new(Next(), Next());
            transforms[i] = new(Next(), Next(), Next(), Next(), Next(), Next());
            if (Optimized(first[i], last[i], transforms[i]) != Reference(first[i], last[i], transforms[i]))
            {
                throw new InvalidOperationException($"Corner oracle mismatch at {i}.");
            }
        }

        // Identical inputs, consumption and operation counts. Exclude setup, verification and warmup.
        for (int i = 0; i < 5; i++)
        {
            Run(false);
            Run(true);
        }

        Console.WriteLine("iteration,variant,ns/op,bytes/op,checksum");
        for (int iteration = 0; iteration < Repetitions; iteration++)
        {
            Measure(iteration, iteration % 2 == 0);
            Measure(iteration, iteration % 2 != 0);
        }

        GC.KeepAlive(_sink);
        return;

        float Next() => (random.NextSingle() - 0.5f) * 100;

        void Measure(int iteration, bool optimized)
        {
            long allocation = GC.GetAllocatedBytesForCurrentThread();
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            Vector4 checksum = Run(optimized);
            long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - start;
            long bytes = GC.GetAllocatedBytesForCurrentThread() - allocation;
            Console.WriteLine($"{iteration},{(optimized ? "optimized" : "old-array-linq")},{elapsed * 1e9 / System.Diagnostics.Stopwatch.Frequency / Operations:F3},{bytes / (double)Operations:F3},{checksum}");
        }

        Vector4 Run(bool optimized)
        {
            Vector4 checksum = Vector4.Zero;
            for (int operation = 0; operation < Operations; operation++)
            {
                int index = operation & 255;
                checksum += optimized ? Optimized(first[index], last[index], transforms[index]) : Reference(first[index], last[index], transforms[index]);
            }

            _sink = checksum;
            return checksum;
        }
#endif
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Vector4 Optimized(Vector2 first, Vector2 last, Matrix3x2 transform)
    {
        NumericsGeometry.GetTransformedBounds(first, last, transform, out Vector2 minimum, out Vector2 maximum);
        return Rectangle(minimum, maximum);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Vector4 Reference(Vector2 first, Vector2 last, Matrix3x2 transform)
    {
        Vector2[] corners = [first, new(last.X, first.Y), new(first.X, last.Y), last];
        return Points(corners.Select(point => Vector2.Transform(point, transform)));
    }

    private static Vector4 Points(IEnumerable<Vector2> points)
    {
        Vector2 minimum = new(float.PositiveInfinity);
        Vector2 maximum = new(float.NegativeInfinity);
        foreach (Vector2 point in points)
        {
            minimum = Vector2.Min(minimum, point);
            maximum = Vector2.Max(maximum, point);
        }

        return Rectangle(minimum, maximum);
    }

    private static Vector4 Rectangle(Vector2 minimum, Vector2 maximum)
        => float.IsPositiveInfinity(minimum.X) ? default : new(minimum.X, minimum.Y, maximum.X - minimum.X, maximum.Y - minimum.Y);
}
