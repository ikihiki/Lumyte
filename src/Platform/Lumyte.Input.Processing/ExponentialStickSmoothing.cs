using System.Numerics;

namespace Lumyte.Input.Processing;

/// <summary>Applies time-based exponential smoothing independently to each controller stick.</summary>
public sealed class ExponentialStickSmoothing : IDeviceDataProcessor
{
    private readonly float _seconds;
    private readonly Vector2[] _values = new Vector2[2];
    private readonly Vector2[] _targets = new Vector2[2];
    private readonly TimeSpan[] _times = new TimeSpan[2];
    private readonly bool[] _initialized = new bool[2];
    private bool _focused = true;

    /// <summary>Initializes a new instance of the <see cref="ExponentialStickSmoothing"/> class.</summary>
    /// <param name="seconds">The positive smoothing time constant.</param>
    public ExponentialStickSmoothing(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds));
        }

        _seconds = seconds;
    }

    /// <summary>Smooths stick samples and resets on focus loss.</summary>
    /// <param name="data">The input batch.</param>
    /// <param name="now">Monotonic sampling time.</param>
    /// <returns>The corrected batch.</returns>
    public IReadOnlyList<InputData> Process(IReadOnlyList<InputData> data, TimeSpan now)
    {
        var result = new List<InputData>(data.Count + 2);
        Span<bool> sampled = stackalloc bool[2];
        foreach (InputData item in data)
        {
            if (item is FocusData { IsFocused: false } or DeviceDisconnectedData)
            {
                Reset();
                _focused = false;
            }

            if (item is FocusData focus)
            {
                _focused = focus.IsFocused;
            }

            if (_focused && item is ControllerStickData stick)
            {
                int index = (int)stick.Stick;
                if ((uint)index >= 2)
                {
                    throw new ArgumentOutOfRangeException(nameof(data));
                }

                if (!float.IsFinite(stick.Value.X) || !float.IsFinite(stick.Value.Y))
                {
                    throw new ArgumentException("Stick values must be finite.", nameof(data));
                }

                sampled[index] = true;
                _targets[index] = stick.Value;
                if (_initialized[index] && now < _times[index])
                {
                    throw new ArgumentOutOfRangeException(nameof(now));
                }

                float amount = _initialized[index] ? 1 - MathF.Exp(-(float)(now - _times[index]).TotalSeconds / _seconds) : 1;
                _values[index] = Vector2.Lerp(_values[index], stick.Value, amount);
                _times[index] = now;
                _initialized[index] = true;
                result.Add(stick with { Value = _values[index] });
            }
            else
            {
                result.Add(item);
            }
        }

        for (int index = 0; index < 2; index++)
        {
            if (!_initialized[index] || sampled[index])
            {
                continue;
            }

            if (now < _times[index])
            {
                throw new ArgumentOutOfRangeException(nameof(now));
            }

            if (_values[index] != _targets[index])
            {
                float amount = 1 - MathF.Exp(-(float)(now - _times[index]).TotalSeconds / _seconds);
                _values[index] = Vector2.Lerp(_values[index], _targets[index], amount);
                result.Add(new ControllerStickData((ControllerStick)index, _values[index]));
            }

            _times[index] = now;
        }

        return result;
    }

    /// <summary>Discards all smoothing history.</summary>
    public void Reset() => Array.Clear(_initialized);
}
