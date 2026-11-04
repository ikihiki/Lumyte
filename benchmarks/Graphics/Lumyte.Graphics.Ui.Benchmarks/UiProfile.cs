namespace Lumyte.Graphics.UiScenarios;

internal sealed record UiProfile(string Id, string Variant, int Hz = 60, int VisibleRows = 20)
{
    internal static readonly UiProfile[] Standard =
    [
        new("U01", "hud-60"),
        new("U01", "hud-120", 120),
        new("U02", "transform"),
        new("U02", "layout"),
        new("U03", "forced-draw"),
        new("U04", "fractional", 60, 20),
        new("U04", "turnover", 60, 20),
        new("U04", "fractional", 60, 100),
        new("U04", "turnover", 60, 100),
        new("U05", "same-width"),
        new("U05", "length"),
        new("U06", "resize"),
        new("U06", "dpi-once"),
        new("U07", "preview-2d"),
    ];

    internal string Key => $"{Id}/{Variant}/{Hz}hz/{VisibleRows}rows";
}
