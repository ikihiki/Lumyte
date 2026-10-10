using System.Numerics;

namespace Lumyte.Input.Processing;

/// <summary>Calibrates stick centers, applies radial dead zones and transforms supported pressure.</summary>
public sealed class AnalogCorrection : IDeviceDataProcessor
{
    private readonly Vector2 _center;
    private readonly float _deadZone;
    private readonly float _pressureExponent;

    /// <summary>Initializes a new instance of the <see cref = "AnalogCorrection"/> class.</summary>
    /// <param name = "center">The center value.</param>
    /// <param name = "deadZone">The deadZone value.</param>
    /// <param name = "pressureExponent">The pressureExponent value.</param>
    public AnalogCorrection(Vector2 center, float deadZone = 0.15f, float pressureExponent = 1)
    {
        if (!float.IsFinite(center.X) || !float.IsFinite(center.Y) || !float.IsFinite(deadZone) || deadZone < 0 || deadZone >= 1 || !float.IsFinite(pressureExponent) || pressureExponent <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(deadZone));
        }

        _center = center;
        _deadZone = deadZone;
        _pressureExponent = pressureExponent;
    }

    /// <summary>Corrects a normalized input batch.</summary>
    /// <param name = "data">The immutable normalized input batch.</param>
    /// <param name = "now">The now value.</param>
    /// <returns>The result of the operation.</returns>
    public IReadOnlyList<InputData> Process(IReadOnlyList<InputData> data, TimeSpan now)
    {
        InputData[]? result = null;
        for (int i = 0; i < data.Count; i++)
        {
            InputData item = data[i];
            InputData corrected = Correct(item);
            if (result is null && !ReferenceEquals(item, corrected))
            {
                result = new InputData[data.Count];
                for (int previous = 0; previous < i; previous++)
                {
                    result[previous] = data[previous];
                }
            }

            if (result is not null)
            {
                result[i] = corrected;
            }
        }

        return result is null ? data : result;
    }

    /// <summary>Discards transient recognition or correction state.</summary>
    public void Reset()
    {
    }

    private InputData Correct(InputData data)
    {
        switch (data)
        {
            case ControllerStickData stick:
                Vector2 value = Correct(stick.Value);
                return value == stick.Value ? stick : stick with { Value = value };
            case TouchData touch:
                float? touchPressure = Pressure(touch.Pressure);
                return touchPressure == touch.Pressure ? touch : touch with { Pressure = touchPressure };
            case PenData pen:
                float? penPressure = Pressure(pen.Pressure);
                return penPressure == pen.Pressure ? pen : pen with { Pressure = penPressure };
            default:
                return data;
        }
    }

    private Vector2 Correct(Vector2 value)
    {
        value -= _center;
        float length = value.Length();
        return length <= _deadZone ? Vector2.Zero : value / length * Math.Clamp((length - _deadZone) / (1 - _deadZone), 0, 1);
    }

    private float? Pressure(float? value)
    {
        if (value is not float pressure)
        {
            return null;
        }

        pressure = Math.Clamp(pressure, 0, 1);
        return _pressureExponent == 1 ? pressure : MathF.Pow(pressure, _pressureExponent);
    }
}
