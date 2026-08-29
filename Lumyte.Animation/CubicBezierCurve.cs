namespace Lumyte.Animation;

public sealed class CubicBezierCurve(float x1, float y1, float x2, float y2) : ICurve
{
    private const float Epsilon = 0.00001f;

    public float X1 { get; } = ValidateControlPoint(x1, nameof(x1));

    public float Y1 { get; } = y1;

    public float X2 { get; } = ValidateControlPoint(x2, nameof(x2));

    public float Y2 { get; } = y2;

    public float Transform(float progress)
    {
        if (progress <= 0f)
        {
            return 0f;
        }

        if (progress >= 1f)
        {
            return 1f;
        }

        var parameter = progress;
        for (var iteration = 0; iteration < 8; iteration++)
        {
            var difference = Sample(parameter, X1, X2) - progress;
            if (MathF.Abs(difference) < Epsilon)
            {
                return Sample(parameter, Y1, Y2);
            }

            var slope = SampleDerivative(parameter, X1, X2);
            if (MathF.Abs(slope) < Epsilon)
            {
                break;
            }

            parameter -= difference / slope;
        }

        var lower = 0f;
        var upper = 1f;
        parameter = progress;
        for (var iteration = 0; iteration < 20; iteration++)
        {
            var value = Sample(parameter, X1, X2);
            if (MathF.Abs(value - progress) < Epsilon)
            {
                break;
            }

            if (value < progress)
            {
                lower = parameter;
            }
            else
            {
                upper = parameter;
            }

            parameter = (lower + upper) * 0.5f;
        }

        return Sample(parameter, Y1, Y2);
    }

    private static float Sample(float parameter, float first, float second)
    {
        var inverse = 1f - parameter;
        return (3f * inverse * inverse * parameter * first)
            + (3f * inverse * parameter * parameter * second)
            + (parameter * parameter * parameter);
    }

    private static float SampleDerivative(float parameter, float first, float second)
    {
        var inverse = 1f - parameter;
        return (3f * inverse * inverse * first)
            + (6f * inverse * parameter * (second - first))
            + (3f * parameter * parameter * (1f - second));
    }

    private static float ValidateControlPoint(float value, string parameterName)
    {
        if (value is < 0f or > 1f)
        {
            throw new ArgumentOutOfRangeException(parameterName, "The horizontal control point must be between zero and one.");
        }

        return value;
    }
}
