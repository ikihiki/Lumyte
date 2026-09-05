using Lumyte.Core.Time;

namespace Lumyte.Animation;

public sealed record PlaybackOptions
{
    private Duration delay;
    private double speed = 1d;

    public Duration Delay
    {
        get => delay;
        init
        {
            if (value < Duration.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(Delay), "Playback delay cannot be negative.");
            }

            delay = value;
        }
    }

    public double Speed
    {
        get => speed;
        init
        {
            if (!double.IsFinite(value) || value <= 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(Speed), "Playback speed must be finite and positive.");
            }

            speed = value;
        }
    }

    public PlaybackLoopMode LoopMode { get; init; }
}
