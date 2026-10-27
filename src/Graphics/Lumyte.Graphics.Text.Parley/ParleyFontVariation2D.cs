namespace Lumyte.Graphics.Text.Parley;

/// <summary>A variable-font axis value in the font's design coordinates.</summary>
/// <param name="Tag">A four-character ASCII axis tag, for example wght or wdth.</param>
/// <param name="Value">The finite requested axis value.</param>
public readonly record struct ParleyFontVariation2D(string Tag, float Value);
