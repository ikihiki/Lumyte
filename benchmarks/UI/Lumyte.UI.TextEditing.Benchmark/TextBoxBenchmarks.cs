using BenchmarkDotNet.Attributes;
using Lumyte.TextEditing;

namespace Lumyte.UI.TextEditing.Benchmarks;

/// <summary>Single-line editor construction and controlled reconciliation through public surfaces.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Screening", "Comparison", "Diagnostic")]
public class TextBoxBenchmarks
{
    private UiSurface _surface = null!;
    private string _text = null!;
    private bool _selectionAlternate;
    private bool _sourceAlternate;

    /// <summary>Gets or sets the bounded single-line length.</summary>
    [Params(32, 512)]
    public int Length { get; set; }

    /// <summary>Creates retained editor state outside measurement.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _text = new string('a', Length);
        _surface = new(new Compose.Definitions.TextBox { Text = _text, Key = "editor" });
        _surface.SetViewport(new(320, 32));
        _surface.UpdateLayout();
    }

    /// <summary>Constructs, initializes, lays out and disposes an actual text box.</summary>
    /// <returns>The initialized document length.</returns>
    [Benchmark]
    public int Construct()
    {
        using var surface = new UiSurface(new Compose.Definitions.TextBox { Text = _text });
        surface.SetViewport(new(320, 32));
        surface.UpdateLayout();
        return ((TextBoxElement)surface.RootElement).Document.CaptureSnapshot().Text.Length;
    }

    /// <summary>Inserts and deletes within the owned editor document, leaving bounded content.</summary>
    /// <returns>The number of published edits in this wave.</returns>
    [Benchmark]
    public int InsertDelete()
    {
        TextDocument document = ((TextBoxElement)_surface.RootElement).Document;
        TextDocumentSnapshot before = document.CaptureSnapshot();
        int middle = Length / 2;
        document.TryReplace(before.Revision, new(middle, 0), "Z", new(middle + 1, middle + 1), TextEditOrigin.Keyboard);
        document.TryReplace(document.CaptureSnapshot().Revision, new(middle, 1), string.Empty, new(middle, middle), TextEditOrigin.Keyboard);
        return document.DrainChanges().Count;
    }

    /// <summary>Changes selection and caret on every invocation.</summary>
    /// <returns>The active caret position.</returns>
    [Benchmark]
    public int SelectionCaret()
    {
        TextDocument document = ((TextBoxElement)_surface.RootElement).Document;
        _selectionAlternate = !_selectionAlternate;
        document.TrySetSelection(document.CaptureSnapshot().Revision, _selectionAlternate ? new(0, Length) : new(Length / 2, Length / 2), TextEditOrigin.Keyboard);
        document.DrainChanges();
        return document.CaptureSnapshot().Selection.Active;
    }

    /// <summary>Rebuilds a keyed declaration with genuinely different external text.</summary>
    /// <returns>The retained document revision.</returns>
    [Benchmark]
    public ulong ReconcileExternalString()
    {
        _sourceAlternate = !_sourceAlternate;
        _surface.Rebuild(new Compose.Definitions.TextBox { Text = _sourceAlternate ? _text + "b" : _text, Key = "editor" });
        _surface.UpdateLayout();
        TextDocument document = ((TextBoxElement)_surface.RootElement).Document;
        document.DrainChanges();
        return document.CaptureSnapshot().Revision;
    }

    /// <summary>Gets the retained public editor for deterministic proof.</summary>
    /// <returns>The live editor.</returns>
    public TextBoxElement GetEditor() => (TextBoxElement)_surface.RootElement;

    /// <summary>Disposes the borrowed-state owner.</summary>
    [GlobalCleanup]
    public void Cleanup() => _surface.Dispose();
}
