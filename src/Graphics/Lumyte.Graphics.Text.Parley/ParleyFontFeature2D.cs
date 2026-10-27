namespace Lumyte.Graphics.Text.Parley;

/// <summary>An OpenType feature override applied during shaping.</summary>
/// <param name="Tag">A four-character ASCII feature tag, for example liga or kern.</param>
/// <param name="Value">The feature value; zero disables the feature.</param>
public readonly record struct ParleyFontFeature2D(string Tag, uint Value = 1);
