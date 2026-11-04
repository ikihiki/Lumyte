using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Lumyte.Graphics.Text.Parley;
using Lumyte.TextEditing;
using Lumyte.UI.TextEditing.Parley;

namespace Lumyte.Graphics.Text.Benchmarks;

internal sealed class TextFixture : IDisposable
{
    private readonly ParleyTextEngine2D _engine;
    private readonly TextDocumentSnapshot[] _documents;
    private readonly ParleyUiTextLayoutProvider _provider;
    private readonly ParleyTextOptions2D _options;

    internal TextFixture(TextCase input)
    {
        long initialization = Stopwatch.GetTimestamp();
        _engine = new ParleyTextEngine2D();
        NativeEngineInitializationMs = Stopwatch.GetElapsedTime(initialization).TotalMilliseconds;
        Input = input;
        try
        {
            long fonts = Stopwatch.GetTimestamp();
            string latin = Register("DejaVuSans.ttf");
            string? extra = input.AdditionalFont is null ? null : Register(input.AdditionalFont);
            FontRegistrationAndHashingMs = Stopwatch.GetElapsedTime(fonts).TotalMilliseconds;
            _options = new ParleyTextOptions2D
            {
                FontFamily = input.Id == "colr" ? extra : latin,
                FallbackFontFamilies = input.Id == "japanese" ? [extra!] : [],
                FontSize = input.Id == "colr" ? 64 : 24,
                LineHeight = 1.2f,
                Locale = input.Id == "bidi" ? "ar" : input.Id == "japanese" ? "ja" : "en",
                BaseDirection = ParleyTextDirection2D.Auto,
                IncludeEditingGeometry = input.Id == "paragraph-width",
            };
            _provider = new ParleyUiTextLayoutProvider(_engine, _options);
            _documents = [new TextDocument(input.First).CaptureSnapshot(), new TextDocument(input.Second).CaptureSnapshot()];
            long anchor = Stopwatch.GetTimestamp();
            Anchor = _engine.CreateLayout("O", new ParleyTextOptions2D { FontFamily = latin, FontSize = 64 });
            FirstAnchorLayoutMs = Stopwatch.GetElapsedTime(anchor).TotalMilliseconds;
        }
        catch
        {
            _engine.Dispose();
            throw;
        }
    }

    internal double NativeEngineInitializationMs { get; }

    internal double FontRegistrationAndHashingMs { get; }

    internal double FirstAnchorLayoutMs { get; }

    internal TextCase Input { get; }

    internal List<FontIdentity> Fonts { get; } = [];

    internal TextLayout2D Anchor { get; }

    public void Dispose() => _engine.Dispose();

    internal TextLayout2D Layout(int state) => Input.Id == "paragraph-width"
        ? _provider.CreateLayout(_documents[state], Input.Width(state)).Drawing
        : Detailed(state).Layout;

    internal ParleyTextLayout2D Detailed(int state) => _engine.CreateDetailedLayout(Input.Text(state), _options with { WrappingLength = Input.Width(state) });

    internal void VerifyScalarCoverage()
    {
        foreach (Rune scalar in (Input.First + Input.Second).EnumerateRunes().Distinct())
        {
            ParleyTextLayout2D layout = _engine.CreateDetailedLayout(scalar.ToString(), _options);
            if (layout.Glyphs.Count == 0 || layout.Glyphs.Any(glyph => glyph.GlyphId == 0))
            {
                throw new InvalidDataException($"Font asset lacks scalar U+{scalar.Value:X}; no missing-glyph substitution accepted.");
            }
        }
    }

    private string Register(string filename)
    {
        byte[] bytes = File.ReadAllBytes(TextCase.FontPath(filename));
        string family = _engine.AddFont(bytes);
        Fonts.Add(new FontIdentity(filename, family, Convert.ToHexString(SHA256.HashData(bytes))));
        return family;
    }
}
