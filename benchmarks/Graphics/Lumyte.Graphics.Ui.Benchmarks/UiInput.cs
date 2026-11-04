using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lumyte.Graphics.UiScenarios;

internal sealed record UiInput(int Frame, int HudValue, int FirstRow, float Scroll, int PanelWidth, int TargetWidth, int TargetHeight, int Dpr, string Text, int UiRevision)
{
    internal string InputId => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this))));

    internal static UiInput At(UiProfile p, int f)
    {
        int hud = f / (p.Hz / 10);
        int row = p.Id == "U04" && p.Variant == "turnover" ? (f / 4) % (10000 - p.VisibleRows) : 0;
        float scroll = p.Id == "U04" ? p.Variant == "fractional" ? (f % 16) * 0.25f : (f % 4) * 0.25f : 0;
        int panel = p.Id == "U02" && p.Variant == "layout" ? 320 + ((f % 60) * 2)
            : p.Id == "U06" && p.Variant == "resize" ? 280 + ((f % 60) * 2)
            : p.Id == "U05" && p.Variant == "wrap" ? 180 + ((f % 40) * 4) : 360;
        int dpr = p.Id == "U06" && p.Variant == "dpi-once" && f >= 30 ? 2 : 1;
        int width = p.Id == "U06" && p.Variant == "resize" ? 1280 + ((f % 30) * 8) : 1920;
        string text = p.Id != "U05" ? "entity.position.x = 0042;"
            : p.Variant == "same-width" ? $"entity.position.x = {f % 10000:D4};"
            : p.Variant == "length" ? "value = " + new string('x', 1 + (f % 24))
        : "A paragraph changes wrapping when its available panel width changes. Stable glyphs and stable content.";
        int revision = p.Id switch
        {
            "U01" => hud,
            "U02" when p.Variant == "layout" => f,
            "U04" => row,
            "U05" => f,
            "U06" => f,
            "U07" => f / 60,
            _ => 0,
        };
        return new(f, hud, row, scroll, panel, width * dpr, 1080 * dpr, dpr, text, revision);
    }
}
