namespace Lumyte.UI.Controls;

internal static class SliderScale
{
    internal static void Validate(double min, double max, double step)
    {
        if (!double.IsFinite(min) || !double.IsFinite(max) || min > max || !double.IsFinite(step) || step <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(min));
        }
    }

    internal static double Fraction(double value, double min, double max)
    {
        if (min == max)
        {
            return 0;
        }

        if (value <= min)
        {
            return 0;
        }

        if (value >= max)
        {
            return 1;
        }

        double scale = Math.Max(Math.Abs(min), Math.Abs(max));
        return Math.Clamp(((value / scale) - (min / scale)) / ((max / scale) - (min / scale)), 0, 1);
    }

    internal static double Interpolate(double min, double max, double fraction)
    {
        if (fraction <= 0)
        {
            return min;
        }

        if (fraction >= 1)
        {
            return max;
        }

        return Math.Clamp(((1 - fraction) * min) + (fraction * max), min, max);
    }

    internal static double Snap(double value, double min, double max, double step)
    {
        value = Math.Clamp(value, min, max);
        if (value == min || value == max)
        {
            return value;
        }

        // Remainders avoid overflowing either the range or the lattice index.
        double remainder = Math.IEEERemainder(value, step) - Math.IEEERemainder(min, step);
        remainder %= step;
        if (remainder == 0)
        {
            return value;
        }

        if (remainder < 0)
        {
            remainder += step;
        }

        double result = remainder < step - remainder ? value - remainder : value + (step - remainder);
        return Math.Clamp(result, min, max);
    }
}
