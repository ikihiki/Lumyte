using Lumyte.Mathematics.Colors;

namespace Lumyte.UI.TextEditing;

/// <summary>Defines ime candidate style.</summary>
/// <param name = "Background">The background.</param>
/// <param name = "Foreground">The foreground.</param>
/// <param name = "SelectedBackground">The selected background.</param>
/// <param name = "SelectedForeground">The selected foreground.</param>
/// <param name = "Padding">The padding.</param>
/// <param name = "RowSpacing">The row spacing.</param>
public sealed record ImeCandidateStyle(Rgba<float> Background, Rgba<float> Foreground, Rgba<float> SelectedBackground, Rgba<float> SelectedForeground, float Padding = 6, float RowSpacing = 2)
{
    /// <summary>Gets the unit straight sRGB Background color.</summary>
    public Rgba<float> Background { get; init => field = UiColorPolicy.Unit(value, nameof(Background)); } = UiColorPolicy.Unit(Background, nameof(Background));

    /// <summary>Gets the unit straight sRGB Foreground color.</summary>
    public Rgba<float> Foreground { get; init => field = UiColorPolicy.Unit(value, nameof(Foreground)); } = UiColorPolicy.Unit(Foreground, nameof(Foreground));

    /// <summary>Gets the unit straight sRGB SelectedBackground color.</summary>
    public Rgba<float> SelectedBackground { get; init => field = UiColorPolicy.Unit(value, nameof(SelectedBackground)); } = UiColorPolicy.Unit(SelectedBackground, nameof(SelectedBackground));

    /// <summary>Gets the unit straight sRGB SelectedForeground color.</summary>
    public Rgba<float> SelectedForeground { get; init => field = UiColorPolicy.Unit(value, nameof(SelectedForeground)); } = UiColorPolicy.Unit(SelectedForeground, nameof(SelectedForeground));

    /// <summary>Gets the default.</summary>
    public static ImeCandidateStyle Default { get; } = new(new(1, 1, 1, 1), new(0, 0, 0, 1), new(0.2f, 0.4f, 0.8f, 1), new(1, 1, 1, 1));
}
