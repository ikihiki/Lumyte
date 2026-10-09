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
    /// <param name = "data">The data value.</param>
    /// <param name = "now">The now value.</param>
    /// <returns>The result of the operation.</returns>
    public IReadOnlyList<InputData> Process(IReadOnlyList<InputData> data, TimeSpan now) => data.Select(item => item switch
    {
        ControllerStickData stick => stick with { Value = Correct(stick.Value) },
        TouchData touch => touch with { Pressure = Pressure(touch.Pressure) },
        PenData pen => pen with { Pressure = Pressure(pen.Pressure) },
        _ => item,
    }).ToArray();

    /// <summary>Discards transient recognition or correction state.</summary>
    public void Reset()
    {
    }

    private Vector2 Correct(Vector2 value)
    {
        value -= _center;
        float length = value.Length();
        return length <= _deadZone ? Vector2.Zero : value / length * Math.Clamp((length - _deadZone) / (1 - _deadZone), 0, 1);
    }

    private float? Pressure(float? value) => value is float pressure ? MathF.Pow(Math.Clamp(pressure, 0, 1), _pressureExponent) : null;
}
