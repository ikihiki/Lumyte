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
    /// <param name="data">The immutable input batch.</param>
    /// <param name="now">Monotonic sampling time.</param>
    /// <returns>The corrected batch.</returns>
    public IReadOnlyList<InputData> Process(IReadOnlyList<InputData> data, TimeSpan now)
    {
        Span<Vector2> values = stackalloc Vector2[2];
        Span<Vector2> targets = stackalloc Vector2[2];
        Span<TimeSpan> times = stackalloc TimeSpan[2];
        Span<bool> initialized = stackalloc bool[2];
        _values.AsSpan().CopyTo(values);
        _targets.AsSpan().CopyTo(targets);
        _times.AsSpan().CopyTo(times);
        _initialized.AsSpan().CopyTo(initialized);
        bool focused = _focused;
        List<InputData>? result = null;
        Span<bool> sampled = stackalloc bool[2];
        for (int i = 0; i < data.Count; i++)
        {
            InputData item = data[i];
            InputData output = item;
            if (item is FocusData { IsFocused: false } or DeviceDisconnectedData)
            {
                initialized.Clear();
                focused = false;
            }

            if (item is FocusData focus)
            {
                focused = focus.IsFocused;
            }

            if (focused && item is ControllerStickData stick)
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
                targets[index] = stick.Value;
                if (initialized[index] && now < times[index])
                {
                    throw new ArgumentOutOfRangeException(nameof(now));
                }

                float amount = initialized[index] ? 1 - MathF.Exp(-(float)(now - times[index]).TotalSeconds / _seconds) : 1;
                values[index] = Vector2.Lerp(values[index], stick.Value, amount);
                times[index] = now;
                initialized[index] = true;
                if (values[index] != stick.Value)
                {
                    output = stick with { Value = values[index] };
                    result ??= CopyPrefix(data, i);
                }
            }

            result?.Add(output);
        }

        for (int index = 0; index < 2; index++)
        {
            if (!initialized[index] || sampled[index])
            {
                continue;
            }

            if (now < times[index])
            {
                throw new ArgumentOutOfRangeException(nameof(now));
            }

            if (values[index] != targets[index])
            {
                float amount = 1 - MathF.Exp(-(float)(now - times[index]).TotalSeconds / _seconds);
                values[index] = Vector2.Lerp(values[index], targets[index], amount);
                result ??= CopyPrefix(data, data.Count);
                result.Add(new ControllerStickData((ControllerStick)index, values[index]));
            }

            times[index] = now;
        }

        values.CopyTo(_values);
        targets.CopyTo(_targets);
        times.CopyTo(_times);
        initialized.CopyTo(_initialized);
        _focused = focused;
        return result is null ? data : result;
    }

    /// <summary>Discards all smoothing history.</summary>
    public void Reset() => Array.Clear(_initialized);

    private static List<InputData> CopyPrefix(IReadOnlyList<InputData> data, int count)
    {
        var result = new List<InputData>(data.Count + 2);
        for (int i = 0; i < count; i++)
        {
            result.Add(data[i]);
        }

        return result;
    }
}
