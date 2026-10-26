namespace Lumyte.Worlds;

/// <summary>A borrowed rendering context extended by the application with its rendering registration target.</summary>
public abstract class DrawContext
{
    /// <summary>Initializes a new instance of the <see cref="DrawContext"/> class.</summary>
    /// <param name="elapsed">The nonnegative real frame duration.</param>
    /// <param name="interpolationAlpha">The finite interpolation fraction in [0, 1], or 1 without interpolation.</param>
    protected DrawContext(TimeSpan elapsed, double interpolationAlpha = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);
        if (!double.IsFinite(interpolationAlpha) || interpolationAlpha < 0 || interpolationAlpha > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(interpolationAlpha));
        }

        Elapsed = elapsed;
        InterpolationAlpha = interpolationAlpha;
    }

    /// <summary>Gets the real frame duration; this does not advance paused simulation.</summary>
    public TimeSpan Elapsed { get; }

    /// <summary>Gets the rendering interpolation fraction. Paused worlds must retain their frozen presentation.</summary>
    public double InterpolationAlpha { get; }
}
