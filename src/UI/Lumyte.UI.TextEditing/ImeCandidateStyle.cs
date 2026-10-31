using Lumyte.Graphics.Drawing2D;

namespace Lumyte.UI.TextEditing;

/// <summary>Defines ime candidate style.</summary>
/// <param name = "Background">The background.</param>
/// <param name = "Foreground">The foreground.</param>
/// <param name = "SelectedBackground">The selected background.</param>
/// <param name = "SelectedForeground">The selected foreground.</param>
/// <param name = "Padding">The padding.</param>
/// <param name = "RowSpacing">The row spacing.</param>
public sealed record ImeCandidateStyle(Color2D Background, Color2D Foreground, Color2D SelectedBackground, Color2D SelectedForeground, float Padding = 6, float RowSpacing = 2)
{
    /// <summary>Gets the default.</summary>
    public static ImeCandidateStyle Default { get; } = new(new(1, 1, 1, 1), new(0, 0, 0, 1), new(0.2f, 0.4f, 0.8f, 1), new(1, 1, 1, 1));
}
