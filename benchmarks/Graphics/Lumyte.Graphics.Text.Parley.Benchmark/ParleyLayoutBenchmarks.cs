using BenchmarkDotNet.Attributes;

namespace Lumyte.Graphics.Text.Parley.Benchmark;

/// <summary>Shaping and outline snapshots with explicit registered font ownership.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Screening", "Comparison", "Diagnostic")]
public class ParleyLayoutBenchmarks
{
    private ParleyTextEngine2D _engine = null!;
    private byte[][] _fonts = null!;
    private string[] _families = null!;
    private string _text = null!;
    private ParleyTextOptions2D _options = null!;
    private ParleyTextLayout2D _cached = null!;

    /// <summary>Gets or sets the named paragraph profile, avoiding a Cartesian parameter grid.</summary>
    [Params("Latin", "Multiscript", "Wrapped")]
    public string Paragraph { get; set; } = "Latin";

    /// <summary>Loads embedded licensed fonts and prepares a retained editing snapshot.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _fonts = new[] { "DejaVuSans", "Japanese", "Khmer" }.Select(name =>
        {
            using Stream stream = typeof(ParleyLayoutBenchmarks).Assembly.GetManifestResourceStream($"Lumyte.Benchmarks.Fonts.{name}.ttf") ?? throw new InvalidOperationException($"Missing embedded font {name}.");
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            return bytes.ToArray();
        }).ToArray();
        _engine = CreateEngine();
        _text = Paragraph switch
        {
            "Latin" => "Office affine AVATAR: reusable outlines.",
            "Multiscript" => "Hello العربية שלום 日本語 ខ្មែរ",
            "Wrapped" => string.Join(' ', Enumerable.Repeat("Office affine editor paragraph wraps with spaces.", 4)),
            _ => throw new InvalidOperationException("Unknown paragraph profile."),
        };
        _options = new ParleyTextOptions2D { WrappingLength = Paragraph == "Wrapped" ? 180 : float.PositiveInfinity, IncludeEditingGeometry = true, FallbackFontFamilies = _families.Skip(1).ToArray() };
        _cached = _engine.CreateDetailedLayout(_text, _options);
    }

    /// <summary>Shapes again with the retained engine's real internal font caches.</summary>
    /// <returns>A new independent vector and editing snapshot.</returns>
    [Benchmark]
    public ParleyTextLayout2D ShapeRetainedEngine() => _engine.CreateDetailedLayout(_text, _options);

    /// <summary>Includes bounded engine construction, font registration, shape and disposal.</summary>
    /// <returns>A snapshot whose engine has already been disposed.</returns>
    [Benchmark]
    public ParleyTextLayout2D ShapeColdEngine()
    {
        using ParleyTextEngine2D engine = CreateEngine();
        return engine.CreateDetailedLayout(_text, _options);
    }

    /// <summary>Copies retained vector output without invoking shaping again.</summary>
    /// <returns>The backend-independent snapshot.</returns>
    [Benchmark]
    public TextLayout2D CopyCachedSnapshot()
    {
        TextLayout2D layout = _cached.Layout;
        return TextLayout2D.FromDrawings(layout.Text, layout.Bounds, layout.AdvanceBounds, layout.LineCount, layout.Drawings);
    }

    /// <summary>Traverses actual public editing geometry, not an invented engine hit-test API.</summary>
    /// <returns>A deterministic observation of caret and selection geometry.</returns>
    [Benchmark]
    public float ReadCachedEditingGeometry()
    {
        float observation = _cached.Glyphs.Count;
        foreach (ParleyCaret2D caret in _cached.Carets)
        {
            observation += caret.Position + caret.Bounds.X + caret.Bounds.Height;
        }

        foreach (ParleySelectionRect2D selection in _cached.SelectionRects)
        {
            observation += selection.Start + selection.Length + selection.Bounds.Width;
        }

        return observation;
    }

    /// <summary>Generates real CPU MTSDF pixels from the first shaped font outline.</summary>
    /// <returns>The encoded distance image; no GPU atlas sampling is implied.</returns>
    [Benchmark]
    public DistanceFieldImage2D GenerateShapedOutlineMtsdf() => DistanceFieldGenerator2D.Generate(_cached.Layout.Geometry[0].Path, 1, 4);

    /// <summary>Releases the retained native engine.</summary>
    [GlobalCleanup]
    public void Cleanup() => _engine.Dispose();

    private ParleyTextEngine2D CreateEngine()
    {
        var engine = new ParleyTextEngine2D();
        try
        {
            _families = _fonts.Select(font => engine.AddFont(font)).ToArray();

            return engine;
        }
        catch
        {
            engine.Dispose();
            throw;
        }
    }
}
