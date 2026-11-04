namespace Lumyte.Graphics.Text.Parley;

/// <summary>Backend-independent paragraph settings consumed by Parley.</summary>
public sealed record ParleyTextOptions2D
{
    /// <summary>Gets a value indicating whether visual caret and selection geometry is included.</summary>
    public bool IncludeEditingGeometry { get; init; }

    /// <summary>Gets the registered primary family; null selects the first registered font.</summary>
    public string? FontFamily { get; init; }

    /// <summary>Gets additional registered fallback families, in priority order.</summary>
    public IReadOnlyList<string> FallbackFontFamilies { get; init; } = [];

    /// <summary>Gets the positive finite em size in logical pixels.</summary>
    public float FontSize { get; init; } = 16;

    /// <summary>Gets the positive wrapping width, or positive infinity for an unbounded line.</summary>
    public float WrappingLength { get; init; } = float.PositiveInfinity;

    /// <summary>Gets the positive finite line-height multiplier relative to the em size.</summary>
    public float LineHeight { get; init; } = 1.2f;

    /// <summary>Gets the alignment inside the wrapping width.</summary>
    public ParleyTextAlignment2D Alignment { get; init; }

    /// <summary>Gets the paragraph base direction.</summary>
    public ParleyTextDirection2D BaseDirection { get; init; }

    /// <summary>Gets an optional BCP 47 language tag used for localized glyph selection and text analysis.</summary>
    public string? Locale { get; init; }

    /// <summary>Gets the zero-based CPAL palette for color glyphs.</summary>
    public int ColorPaletteIndex { get; init; }

    /// <summary>Gets the decorations applied unless a text run overrides them.</summary>
    public ParleyTextDecorations2D Decorations { get; init; }

    /// <summary>Gets OpenType shaping overrides, copied when creating a layout.</summary>
    public IReadOnlyList<ParleyFontFeature2D> FontFeatures { get; init; } = [];

    /// <summary>Gets variable-font axis values, applied consistently to shaping, outlines and color paints.</summary>
    public IReadOnlyList<ParleyFontVariation2D> FontVariations { get; init; } = [];

    /// <summary>Gets optional styled UTF-16 ranges, copied when creating a layout.</summary>
    public IReadOnlyList<ParleyTextRun2D> TextRuns { get; init; } = [];
}
