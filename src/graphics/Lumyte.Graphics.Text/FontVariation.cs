namespace Lumyte.Graphics.Text;

/// <summary>Identifies one OpenType variation axis and its design-space value.</summary>
public readonly record struct FontVariation
{
    /// <summary>Creates an immutable OpenType variation setting.</summary>
    /// <param name="tag">The axis's four-character printable ASCII OpenType tag.</param>
    /// <param name="value">The finite design-space value assigned to the axis.</param>
    public FontVariation(string tag, float value)
    {
        ValidateTag(tag, nameof(tag));
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        Tag = tag;
        Value = value;
    }

    /// <summary>The four-character OpenType axis tag, such as <c>wght</c>.</summary>
    public string Tag { get; }

    /// <summary>The axis value in the font's design coordinate space.</summary>
    public float Value { get; }

    internal uint ToOpenTypeTag(string parameterName)
    {
        ValidateTag(Tag, parameterName);
        if (!float.IsFinite(Value))
        {
            throw new ArgumentException("Font variation values must be finite.", parameterName);
        }

        return ((uint)Tag[0] << 24)
            | ((uint)Tag[1] << 16)
            | ((uint)Tag[2] << 8)
            | Tag[3];
    }

    private static void ValidateTag(string? tag, string parameterName)
    {
        if (tag is null || tag.Length != 4)
        {
            throw new ArgumentException("An OpenType variation tag must contain exactly four characters.", parameterName);
        }

        foreach (char character in tag)
        {
            if (character is < ' ' or > '~')
            {
                throw new ArgumentException(
                    "An OpenType variation tag must contain only printable ASCII characters.",
                    parameterName);
            }
        }
    }
}
