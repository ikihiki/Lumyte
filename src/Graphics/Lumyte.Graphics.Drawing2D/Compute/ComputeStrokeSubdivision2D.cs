using System.Numerics;

namespace Lumyte.Graphics.Drawing2D;

// Exact-arithmetic geometric bound for the CURRENT shader's logical chord-normal
// bodies, not analytic offset vertices. Float32 evaluation/normalization error is
// NOT included; the device hull guard alone does not bound short-chord direction error.
// BODY + BEVEL + BUTT: E <= D/(8N²) + S*w*M/(2*m*N).
// Other styles retain functional coverage; this is not a theorem for their join/cap tips.
internal static class ComputeStrokeSubdivision2D
{
    internal static int Count(Vector2 a, Vector2 b, Vector2 c, Vector2 d, bool cubic, Matrix3x2 transform, float width)
    {
        ReadOnlySpan<Vector2> points = [a, b, c, d];
        int degree = cubic ? 3 : 2;
        if (ConstantRay(points, degree))
        {
            // Includes collapsed curves and zero derivative controls: their geometric
            // image is a segment or point, so the existing point/cap behavior applies.
            return 1;
        }

        Span<BoundVector> derivatives = stackalloc BoundVector[3];
        for (int i = 0; i < degree; i++)
        {
            derivatives[i] = new(
                Interval.Difference(points[i + 1].X, points[i].X).Multiply(degree),
                Interval.Difference(points[i + 1].Y, points[i].Y).Multiply(degree));
        }

        double speed = 0;

        // A closest hull vertex/edge proposes a separating direction. Only the
        // independently outward-rounded dot certificate is trusted, never its distance.
        for (int i = 0; i < degree; i++)
        {
            double x = derivatives[i].X.Midpoint;
            double y = derivatives[i].Y.Midpoint;
            speed = Math.Max(speed, Certify(derivatives[..degree], x, y));
            for (int j = i + 1; j < degree; j++)
            {
                double dx = derivatives[j].X.Midpoint - x;
                double dy = derivatives[j].Y.Midpoint - y;
                double t = Math.Clamp(-((x * dx) + (y * dy)) / ((dx * dx) + (dy * dy)), 0, 1);
                speed = Math.Max(speed, Certify(derivatives[..degree], x + (t * dx), y + (t * dy)));
            }
        }

        if (!(speed > 0))
        {
            throw new NotSupportedException("Stroke derivative hull has no certified positive speed; cusp/turnaround requires raw-curve splitting.");
        }

        double logicalAcceleration = 0;
        double deviceAcceleration = 0;
        for (int i = 0; i < degree - 1; i++)
        {
            var acceleration = new BoundVector(
                derivatives[i + 1].X.Subtract(derivatives[i].X).Multiply(degree - 1),
                derivatives[i + 1].Y.Subtract(derivatives[i].Y).Multiply(degree - 1));
            logicalAcceleration = Math.Max(logicalAcceleration, acceleration.NormUpper);
            var device = new BoundVector(
                acceleration.X.Multiply(transform.M11).Add(acceleration.Y.Multiply(transform.M21)),
                acceleration.X.Multiply(transform.M12).Add(acceleration.Y.Multiply(transform.M22)));
            deviceAcceleration = Math.Max(deviceAcceleration, device.NormUpper);
        }

        double scale = Up(Math.Sqrt(Up(Up(Up((double)transform.M11 * transform.M11) + Up((double)transform.M12 * transform.M12)) +
            Up(Up((double)transform.M21 * transform.M21) + Up((double)transform.M22 * transform.M22)))));
        double termA = Up(deviceAcceleration / 8);
        double termB = Up(Up(Up(scale * (width * 0.5)) * logicalAcceleration) / Down(2 * speed));
        const double Epsilon = 0.125;
        double n = Math.Max(1, Math.Ceiling(Up((termB + Up(Math.Sqrt(Up(Up(termB * termB) + Up(4 * Epsilon * termA))))) / (2 * Epsilon))));
        while (double.IsFinite(n) && n <= 65536 && Up(Up(termA / (n * n)) + Up(termB / n)) > Epsilon)
        {
            n++;
        }

        if (!double.IsFinite(n) || n > 65536)
        {
            throw new NotSupportedException("Stroke exceeds the tangent-error subdivision budget.");
        }

        return (int)n;
    }

    private static double Certify(ReadOnlySpan<BoundVector> controls, double x, double y)
    {
        double norm = new BoundVector(new(x, x), new(y, y)).NormUpper;
        if (!(norm > 0) || !double.IsFinite(norm))
        {
            return 0;
        }

        double lower = double.PositiveInfinity;
        foreach (BoundVector control in controls)
        {
            lower = Math.Min(lower, control.X.Multiply(x).Add(control.Y.Multiply(y)).Lower);
        }

        return lower > 0 ? Math.Max(0, Down(lower / norm)) : 0;
    }

    private static bool ConstantRay(ReadOnlySpan<Vector2> points, int degree)
    {
        BigInteger firstX = 0;
        BigInteger firstY = 0;
        for (int i = 0; i < degree; i++)
        {
            BigInteger x = Exact(points[i + 1].X) - Exact(points[i].X);
            BigInteger y = Exact(points[i + 1].Y) - Exact(points[i].Y);
            if (firstX.IsZero && firstY.IsZero)
            {
                firstX = x;
                firstY = y;
            }
            else if ((firstX * y) != (firstY * x) || ((firstX * x) + (firstY * y)).Sign < 0)
            {
                return false;
            }
        }

        return true;
    }

    // Every finite binary32 is an integer multiple of 2^-149. Exact predicates
    // avoid classifying a nearly collinear turnaround as a constant normal.
    private static BigInteger Exact(float value)
    {
        if (!float.IsFinite(value))
        {
            throw new NotSupportedException("Curve points must be finite.");
        }

        uint bits = BitConverter.SingleToUInt32Bits(value);
        int exponent = (int)((bits >> 23) & 255);
        BigInteger mantissa = (bits & 0x7FFFFF) | (exponent == 0 ? 0U : 0x800000U);
        BigInteger result = mantissa << Math.Max(0, exponent - 1);
        return (bits & 0x80000000) != 0 ? -result : result;
    }

    private static double Up(double value) => Math.BitIncrement(value);

    private static double Down(double value) => Math.BitDecrement(value);

    private readonly record struct BoundVector(Interval X, Interval Y)
    {
        internal double NormUpper
        {
            get
            {
                double x = Math.Max(Math.Abs(X.Lower), Math.Abs(X.Upper));
                double y = Math.Max(Math.Abs(Y.Lower), Math.Abs(Y.Upper));
                return Up(Math.Sqrt(Up(Up(x * x) + Up(y * y))));
            }
        }
    }

    private readonly record struct Interval(double Lower, double Upper)
    {
        internal double Midpoint => (Lower * 0.5) + (Upper * 0.5);

        internal static Interval Difference(double a, double b) => new(Down(a - b), Up(a - b));

        internal Interval Subtract(Interval b) => new(Down(Lower - b.Upper), Up(Upper - b.Lower));

        internal Interval Add(Interval b) => new(Down(Lower + b.Lower), Up(Upper + b.Upper));

        internal Interval Multiply(double b) => b >= 0 ? new(Down(Lower * b), Up(Upper * b)) : new(Down(Upper * b), Up(Lower * b));
    }
}
