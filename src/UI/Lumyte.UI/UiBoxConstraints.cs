using System.Numerics;

namespace Lumyte.UI;

/// <summary>Specifies nonnegative box dimensions with finite minima and optionally unbounded maxima.</summary>
public readonly record struct UiBoxConstraints
{
    /// <summary>Initializes a new instance of the <see cref="UiBoxConstraints"/> struct.</summary>
    /// <param name="minWidth">The finite minimum width.</param>
    /// <param name="maxWidth">The maximum width, including positive infinity.</param>
    /// <param name="minHeight">The finite minimum height.</param>
    /// <param name="maxHeight">The maximum height, including positive infinity.</param>
    public UiBoxConstraints(float minWidth, float maxWidth, float minHeight, float maxHeight)
    {
        UiLayoutMath.ValidateDimension(minWidth, nameof(minWidth));
        UiLayoutMath.ValidateDimension(minHeight, nameof(minHeight));
        if (float.IsNaN(maxWidth) || maxWidth < minWidth)
        {
            throw new ArgumentOutOfRangeException(nameof(maxWidth));
        }

        if (float.IsNaN(maxHeight) || maxHeight < minHeight)
        {
            throw new ArgumentOutOfRangeException(nameof(maxHeight));
        }

        MinWidth = minWidth;
        MaxWidth = maxWidth;
        MinHeight = minHeight;
        MaxHeight = maxHeight;
    }

    /// <summary>Gets the minimum width.</summary>
    public float MinWidth { get; }

    /// <summary>Gets the maximum width.</summary>
    public float MaxWidth { get; }

    /// <summary>Gets the minimum height.</summary>
    public float MinHeight { get; }

    /// <summary>Gets the maximum height.</summary>
    public float MaxHeight { get; }

    /// <summary>Gets a value indicating whether the maximum width is finite.</summary>
    public bool HasBoundedWidth => float.IsFinite(MaxWidth);

    /// <summary>Gets a value indicating whether the maximum height is finite.</summary>
    public bool HasBoundedHeight => float.IsFinite(MaxHeight);

    /// <summary>Gets a value indicating whether both dimensions are fixed.</summary>
    public bool IsTight => MinWidth == MaxWidth && MinHeight == MaxHeight;

    /// <summary>Creates fixed, finite dimensions.</summary>
    /// <param name="size">The nonnegative finite dimensions.</param>
    /// <returns>The tight constraints.</returns>
    public static UiBoxConstraints Tight(Vector2 size)
    {
        UiLayoutMath.ValidateSize(size, nameof(size));
        return new(size.X, size.X, size.Y, size.Y);
    }

    /// <summary>Creates constraints with zero minima.</summary>
    /// <param name="maximum">The maxima, including positive infinity.</param>
    /// <returns>The loose constraints.</returns>
    public static UiBoxConstraints Loose(Vector2 maximum) => new(0, maximum.X, 0, maximum.Y);

    /// <summary>Clears minima while retaining the maxima.</summary>
    /// <returns>The loose constraints.</returns>
    public UiBoxConstraints Loosen() => new(0, MaxWidth, 0, MaxHeight);

    /// <summary>Fixes specified dimensions after clamping them into these constraints.</summary>
    /// <param name="width">A finite nonnegative width, or null to preserve the width constraints.</param>
    /// <param name="height">A finite nonnegative height, or null to preserve the height constraints.</param>
    /// <returns>The tightened constraints.</returns>
    public UiBoxConstraints Tighten(float? width = null, float? height = null)
    {
        if (width.HasValue)
        {
            UiLayoutMath.ValidateDimension(width.Value, nameof(width));
        }

        if (height.HasValue)
        {
            UiLayoutMath.ValidateDimension(height.Value, nameof(height));
        }

        float constrainedWidth = Math.Clamp(width.GetValueOrDefault(), MinWidth, MaxWidth);
        float constrainedHeight = Math.Clamp(height.GetValueOrDefault(), MinHeight, MaxHeight);
        return new(
            width.HasValue ? constrainedWidth : MinWidth,
            width.HasValue ? constrainedWidth : MaxWidth,
            height.HasValue ? constrainedHeight : MinHeight,
            height.HasValue ? constrainedHeight : MaxHeight);
    }

    /// <summary>Clamps a nonnegative finite candidate into the constraints.</summary>
    /// <param name="size">The candidate dimensions.</param>
    /// <returns>The constrained finite dimensions.</returns>
    public Vector2 Constrain(Vector2 size)
    {
        UiLayoutMath.ValidateSize(size, nameof(size));
        return new(Math.Clamp(size.X, MinWidth, MaxWidth), Math.Clamp(size.Y, MinHeight, MaxHeight));
    }
}
