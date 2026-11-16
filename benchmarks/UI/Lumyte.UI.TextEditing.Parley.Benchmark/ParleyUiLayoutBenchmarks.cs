using BenchmarkDotNet.Attributes;
using Lumyte.Graphics.Text.Parley;
using Lumyte.TextEditing;

namespace Lumyte.UI.TextEditing.Parley.Benchmarks;

/// <summary>Native CPU shaping plus UI grapheme, selection and visual-caret adaptation.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Screening", "Comparison", "Diagnostic")]
public class ParleyUiLayoutBenchmarks
{
    private ParleyTextEngine2D _engine = null!;
    private ParleyUiTextLayoutProvider _provider = null!;
    private TextDocument _document = null!;
    private byte[] _latinFont = null!;
    private byte[] _japaneseFont = null!;
    private bool _alternate;

    /// <summary>Gets or sets the script and geometry workload.</summary>
    [Params("Latin", "Japanese", "Bidi", "Grapheme")]
    public string Script { get; set; } = "Latin";

    /// <summary>Registers explicit fonts and warms the native engine outside measurement.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _latinFont = ReadFont("DejaVuSans.ttf");
        _japaneseFont = ReadFont("LumyteSampleJapanese.ttf");
        _engine = new();
        _provider = CreateProvider(_engine);
        _document = new(Script switch
        {
            "Japanese" => string.Concat(Enumerable.Repeat("日本語の文字", 8)),
            "Bidi" => string.Concat(Enumerable.Repeat("abc אבג def ", 8)),
            "Grapheme" => string.Concat(Enumerable.Repeat("A😀e\u0301Z ", 8)),
            _ => string.Concat(Enumerable.Repeat("office AV fi ", 8)),
        });
        _provider.CreateLayout(_document.CaptureSnapshot(), 160);
    }

    /// <summary>Adapts repeated text using warmed font and glyph caches, including wrapping.</summary>
    /// <returns>The copied UI layout.</returns>
    [Benchmark]
    public UiTextLayout WarmWrappedLayout() => _provider.CreateLayout(_document.CaptureSnapshot(), 160);

    /// <summary>Explicitly includes cold engine creation, font registration and UI layout adaptation.</summary>
    /// <returns>The engine-independent copied UI layout.</returns>
    [Benchmark]
    public UiTextLayout ColdEngineLayout()
    {
        using var engine = new ParleyTextEngine2D();
        return CreateProvider(engine).CreateLayout(_document.CaptureSnapshot(), 160);
    }

    /// <summary>Alternates a real edit then materializes revision-bound caret and range metadata.</summary>
    /// <returns>The copied current UI layout.</returns>
    [Benchmark]
    public UiTextLayout EditMetadata()
    {
        _alternate = !_alternate;
        TextDocumentSnapshot snapshot = _document.CaptureSnapshot();
        _document.TryReplace(snapshot.Revision, new(0, 1), _alternate ? "Q" : "R", new(1, 1), TextEditOrigin.Keyboard);
        _document.DrainChanges();
        UiTextLayout layout = _provider.CreateLayout(_document.CaptureSnapshot(), float.PositiveInfinity);
        layout.GetRangeBounds(new(0, Math.Min(16, _document.CaptureSnapshot().Text.Length)));
        UiTextCaret hit = layout.HitTest(new(30, 10));
        layout.GetCaretBounds(hit);
        layout.MoveCaret(hit, true);
        return layout;
    }

    /// <summary>Gets current source metadata for deterministic proof.</summary>
    /// <returns>The current snapshot.</returns>
    public TextDocumentSnapshot CaptureDocument() => _document.CaptureSnapshot();

    /// <summary>Releases the native CPU owner.</summary>
    [GlobalCleanup]
    public void Cleanup() => _engine.Dispose();

    private static byte[] ReadFont(string name)
    {
        using Stream stream = typeof(ParleyUiLayoutBenchmarks).Assembly.GetManifestResourceStream("Lumyte.UI.TextEditing.Parley.Benchmark.Fonts." + name)
            ?? throw new InvalidOperationException("Missing embedded benchmark font: " + name);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private ParleyUiTextLayoutProvider CreateProvider(ParleyTextEngine2D engine)
    {
        string latin = engine.AddFont(_latinFont);
        string japanese = engine.AddFont(_japaneseFont);
        return new(engine, new() { FontFamily = latin, FallbackFontFamilies = [japanese], FontSize = 18 });
    }
}
