namespace Lumyte.Mathematics;

/// <summary>Provides inverse-horizontal cubic Bezier evaluation.</summary>
public sealed class CubicBezierTiming
{
    private readonly double _x1;
    private readonly double _y1;
    private readonly double _x2;
    private readonly double _y2;

    /// <summary>Initializes a new instance of the <see cref="CubicBezierTiming"/> class. Creates a cubic curve from (0, 0) to (1, 1).</summary>
    /// <param name="x1">First horizontal control in [0, 1].</param>
    /// <param name="y1">First finite vertical control.</param>
    /// <param name="x2">Second horizontal control in [0, 1].</param>
    /// <param name="y2">Second finite vertical control.</param>
    public CubicBezierTiming(double x1, double y1, double x2, double y2)
    {
        ValidateAmount(x1);
        ValidateAmount(x2);
        ArgumentOutOfRangeException.ThrowIfNotEqual(double.IsFinite(y1), true, nameof(y1));
        ArgumentOutOfRangeException.ThrowIfNotEqual(double.IsFinite(y2), true, nameof(y2));
        _x1 = x1;
        _y1 = y1;
        _x2 = x2;
        _y2 = y2;
    }

    /// <summary>Evaluates the vertical coordinate at the specified horizontal coordinate.</summary>
    /// <param name="amount">A finite horizontal coordinate in [0, 1].</param>
    /// <returns>The vertical coordinate, which may overshoot.</returns>
    public double Transform(double amount)
    {
        ValidateAmount(amount);
        if (amount is 0 or 1)
        {
            return amount;
        }

        double lower = 0;
        double upper = 1;
        double t = amount;
        for (int iteration = 0; iteration < 8; iteration++)
        {
            double x = Evaluate(t, _x1, _x2);
            if (x == amount)
            {
                return Evaluate(t, _y1, _y2);
            }

            if (x < amount)
            {
                lower = t;
            }
            else
            {
                upper = t;
            }

            double inverse = 1 - t;
            double slope = (3 * inverse * inverse * _x1) + (6 * inverse * t * (_x2 - _x1)) + (3 * t * t * (1 - _x2));
            double next = t - ((x - amount) / slope);
            t = double.IsFinite(next) && next > lower && next < upper ? next : (lower + upper) / 2;
        }

        // Bounded bisection also handles zero derivatives and coincident controls.
        for (int iteration = 0; iteration < 48; iteration++)
        {
            double x = Evaluate(t, _x1, _x2);
            if (x == amount)
            {
                break;
            }

            if (x < amount)
            {
                lower = t;
            }
            else
            {
                upper = t;
            }

            t = (lower + upper) / 2;
        }

        return Evaluate(t, _y1, _y2);
    }

    internal static void ValidateAmount(double amount)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(double.IsFinite(amount), true, nameof(amount));
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(amount, 1);
    }

    private static double Evaluate(double t, double control1, double control2)
    {
        double inverse = 1 - t;
        return (3 * inverse * inverse * t * control1) + (3 * inverse * t * t * control2) + (t * t * t);
    }
}
