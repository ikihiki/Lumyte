namespace Lumyte.UI;

internal static class SplitViewLayout
{
    internal static float[] Allocate(float length, float divider, IReadOnlyList<double> weights, IReadOnlyList<float> minimums, out float gap)
    {
        int count = weights.Count;
        gap = count > 1 ? (float)Math.Min(divider, (double)length / (count - 1)) : 0;
        double available = Math.Max(0, length - ((double)gap * Math.Max(0, count - 1)));
        double minimum = minimums.Sum(value => (double)value);
        double total = weights.Sum();
        double shrink = minimum > available ? available / minimum : 1;
        double extra = Math.Max(0, available - minimum);
        float[] sizes = new float[count];
        double position = 0;
        for (int index = 0; index < count; index++)
        {
            double desired = (minimums[index] * shrink) + (total > 0 ? extra * (weights[index] / total) : extra / count);
            float start = (float)Math.Min(length, position);
            double remaining = Math.Max(0, (double)length - start);
            float extent = (float)Math.Min(remaining, desired);
            if (extent > remaining)
            {
                extent = MathF.BitDecrement(extent);
            }

            sizes[index] = Math.Max(0, extent);
            position = (double)start + sizes[index] + gap;
        }

        return sizes;
    }
}
