namespace Lumyte.UI;

/// <summary>An immutable fixed length or positive proportional grid track.</summary>
public readonly record struct UiGridTrack
{
    /// <summary>Initializes a new instance of the <see cref="UiGridTrack"/> struct.</summary>
    /// <param name="value">The finite nonnegative length, or positive ratio.</param>
    /// <param name="isRatio">Whether the value is a proportional weight.</param>
    public UiGridTrack(float value, bool isRatio = false)
    {
        UiLayoutMath.ValidateDimension(value, nameof(value));
        if (isRatio && value == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        Value = value;
        IsRatio = isRatio;
    }

    /// <summary>Gets the length or weight. Default is a zero fixed track.</summary>
    public float Value { get; }

    /// <summary>Gets a value indicating whether the track shares remaining space.</summary>
    public bool IsRatio { get; }
}
