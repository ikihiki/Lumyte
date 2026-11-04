namespace Lumyte.Graphics.Text.Benchmarks;

internal sealed record TextCase(string Id, string First, string Second, string? AdditionalFont = null)
{
    internal static IReadOnlyList<TextCase> All { get; } =
    [
        new("fixed", "HUD OAV Lumyte", "HUD OAV Lumyte"),
        new("numeric", "0123456789 score 100", "0123456789 score 101"),
        new("new-glyph", "HUD 0123", "HUD 0123 Ω"),
        new("bidi", "HUD 123 العربية", "HUD 124 العربية"),
        new("paragraph-width", "alpha beta gamma delta epsilon zeta eta theta iota kappa", "alpha beta gamma delta epsilon zeta eta theta iota kappa"),
        new("japanese", "日本語", "日本語", "LumyteSampleJapanese.ttf"),
        new("colr", "\U000F0500", "\U000F0500", "ColorFontTest.ttf"),
    ];

    internal bool Available => File.Exists(FontPath("DejaVuSans.ttf")) && (AdditionalFont is null || File.Exists(FontPath(AdditionalFont)));

    internal bool Changes => First != Second || Id == "paragraph-width";

    internal static string FontPath(string filename) => Path.Combine(AppContext.BaseDirectory, "Fonts", filename);

    internal static TextCase Find(string id) => All.Single(item => item.Id == id);

    internal string Text(int state) => state == 0 ? First : Second;

    internal float Width(int state) => Id == "paragraph-width" ? state == 0 ? 180 : 360 : 440;
}
