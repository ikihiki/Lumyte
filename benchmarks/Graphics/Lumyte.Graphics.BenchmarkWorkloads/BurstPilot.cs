namespace Lumyte.Graphics.BenchmarkWorkloads;

/// <summary>Phase-matched periodic stability, never diluted by long static intervals.</summary>
public static class BurstPilot
{
    /// <summary>Gets a complete return-to-identical-state cycle in logical frames.</summary>
    public const int CycleFrames = RectangleTrace.BurstPeriod * 2;

    /// <summary>Gets the four-cycle stability observation window.</summary>
    public const int WindowFrames = CycleFrames * 4;

    /// <summary>Checks the last four observations of every exact phase independently.</summary>
    /// <param name="values">Consecutive logical-frame observations from one process.</param>
    /// <returns>True only when all 240 phase ratios are finite, positive and at most 1.05.</returns>
    public static bool Stable(IReadOnlyList<double> values)
    {
        if (values.Count < WindowFrames)
        {
            return false;
        }

        int first = values.Count - WindowFrames;
        for (int phase = 0; phase < CycleFrames; phase++)
        {
            double minimum = double.PositiveInfinity;
            double maximum = 0;
            for (int cycle = 0; cycle < 4; cycle++)
            {
                double value = values[first + phase + (cycle * CycleFrames)];
                if (!double.IsFinite(value) || value <= 0)
                {
                    return false;
                }

                minimum = Math.Min(minimum, value);
                maximum = Math.Max(maximum, value);
            }

            if (maximum / minimum > 1.05)
            {
                return false;
            }
        }

        return true;
    }
}
