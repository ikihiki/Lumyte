namespace Lumyte.Graphics.Drawing2D;

/// <summary>Specifies the 28 OpenType COLR compositing operators, applied to isolated source and backdrop paints.</summary>
public enum CompositeMode2D
{
    /// <summary>Clears both operands.</summary>
    Clear,

    /// <summary>Keeps the source.</summary>
    Source,

    /// <summary>Keeps the backdrop.</summary>
    Destination,

    /// <summary>Places the source over the backdrop.</summary>
    SourceOver,

    /// <summary>Places the backdrop over the source.</summary>
    DestinationOver,

    /// <summary>Keeps source inside the backdrop.</summary>
    SourceIn,

    /// <summary>Keeps backdrop inside the source.</summary>
    DestinationIn,

    /// <summary>Keeps source outside the backdrop.</summary>
    SourceOut,

    /// <summary>Keeps backdrop outside the source.</summary>
    DestinationOut,

    /// <summary>Places source atop the backdrop.</summary>
    SourceAtop,

    /// <summary>Places backdrop atop the source.</summary>
    DestinationAtop,

    /// <summary>Keeps each operand outside the other.</summary>
    Xor,

    /// <summary>Adds and clamps both premultiplied operands.</summary>
    Plus,

    /// <summary>Uses the screen blend function.</summary>
    Screen,

    /// <summary>Uses the overlay blend function.</summary>
    Overlay,

    /// <summary>Uses the darker component from each operand.</summary>
    Darken,

    /// <summary>Uses the lighter component from each operand.</summary>
    Lighten,

    /// <summary>Uses the color-dodge blend function.</summary>
    ColorDodge,

    /// <summary>Uses the color-burn blend function.</summary>
    ColorBurn,

    /// <summary>Uses the hard-light blend function.</summary>
    HardLight,

    /// <summary>Uses the soft-light blend function.</summary>
    SoftLight,

    /// <summary>Uses the absolute component difference.</summary>
    Difference,

    /// <summary>Uses the exclusion blend function.</summary>
    Exclusion,

    /// <summary>Multiplies the operand components.</summary>
    Multiply,

    /// <summary>Uses source hue with backdrop saturation and luminosity.</summary>
    Hue,

    /// <summary>Uses source saturation with backdrop hue and luminosity.</summary>
    Saturation,

    /// <summary>Uses source hue and saturation with backdrop luminosity.</summary>
    Color,

    /// <summary>Uses source luminosity with backdrop hue and saturation.</summary>
    Luminosity,
}
