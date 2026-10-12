namespace Lumyte.Graphics.ImageEffects;

/// <summary>Stores four immutable rows of RGBA and constant-term coefficients.</summary>
public sealed class ColorMatrix4x5
{
    /// <summary>Initializes a new instance of the <see cref="ColorMatrix4x5"/> class by copying twenty finite row-major coefficients.</summary>
    /// <param name="coefficients">The twenty matrix coefficients.</param>
    public ColorMatrix4x5(ReadOnlySpan<float> coefficients)
    {
        if (coefficients.Length != 20)
        {
            throw new ArgumentException("A color matrix requires twenty coefficients.", nameof(coefficients));
        }

        foreach (float value in coefficients)
        {
            if (!float.IsFinite(value))
            {
                throw new ArgumentException("Color matrix coefficients must be finite.", nameof(coefficients));
            }
        }

        Coefficients = Array.AsReadOnly(coefficients.ToArray());
    }

    /// <summary>Gets the copied row-major coefficients.</summary>
    public IReadOnlyList<float> Coefficients { get; }
}
