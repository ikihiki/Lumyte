using BenchmarkDotNet.Attributes;

namespace Lumyte.TextEditing.Benchmark;

/// <summary>Single-line model work with bounded change history and nonidentical edits.</summary>
[BenchmarkCategory("Core", "TextEditing", "Screening", "Comparison", "Diagnostic")]
public class TextBenchmarks
{
    private readonly LogicalNavigator _navigator = new();
    private TextDocument _document = null!;
    private string _input = string.Empty;
    private bool _alternate;

    /// <summary>Gets or sets document code-unit length.</summary>
    [Params(256, 4096, 32768)]
    public int Length { get; set; }

    /// <summary>Builds a reusable mixed-script input outside measurement.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _input = string.Concat(Enumerable.Repeat("a日e\u0301", Length / 4));
        _document = new TextDocument(_input, Length + 64);
    }

    /// <summary>Bounds retained change history outside each measured iteration.</summary>
    [IterationCleanup]
    public void Drain() => _document.DrainChanges();

    /// <summary>Constructs a normalized document.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    public TextDocument Construct() => new(_input, Length + 64);

    /// <summary>Normalizes line endings without changing normalization form.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    public string Normalize() => TextDocument.Normalize(_input);

    /// <summary>Finds extended grapheme boundaries.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    public int[] Boundaries() => TextDocument.GraphemeBoundaries(_input);

    /// <summary>Commits an equal-length content-changing transaction and drains its record.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    public ulong Replace()
    {
        using TextDocumentTransaction edit = _document.BeginEdit(TextEditOrigin.Keyboard);
        _alternate = !_alternate;
        edit.Replace(new TextSpan(0, 1), _alternate ? "b" : "a", new TextSelection(1, 1));
        edit.Commit();
        _document.DrainChanges();
        return _document.CaptureSnapshot().Revision;
    }

    /// <summary>Abandons a candidate change without publishing state.</summary>
    [Benchmark]
    public void Rollback()
    {
        using TextDocumentTransaction edit = _document.BeginEdit(TextEditOrigin.Keyboard);
        edit.Replace(new TextSpan(0, 1), "z", new TextSelection(1, 1));
    }

    /// <summary>Starts and cancels a composition lifetime from a fresh checkpoint.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    public string CompositionLifetime()
    {
        var document = new TextDocument(_input, Length + 64);
        using (TextDocumentTransaction edit = document.BeginEdit(TextEditOrigin.Keyboard))
        {
            edit.SetComposition(new TextCompositionSnapshot(new Guid("11111111-2222-3333-4444-555555555555"), new TextSpan(0, 16), Array.Empty<TextCompositionSpan>()));
            edit.Commit();
        }

        using (TextDocumentTransaction edit = document.BeginEdit(TextEditOrigin.Keyboard))
        {
            edit.EndComposition(TextCompositionEnd.Cancel);
            edit.Commit();
        }

        return document.CaptureSnapshot().Text;
    }

    /// <summary>Deletes a known combined grapheme on a fresh document.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    public string DeleteGrapheme()
    {
        var document = new TextDocument("ae\u0301日");
        document.TrySetSelection(0, new TextSelection(3, 3), TextEditOrigin.Keyboard);
        TextEditingCommands.Execute(document, _navigator, TextEditCommand.DeleteBackward);
        return document.CaptureSnapshot().Text;
    }

    /// <summary>Navigates logically to alternating document endpoints.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    public TextSelection Navigate()
    {
        _alternate = !_alternate;
        TextEditingCommands.Execute(_document, _navigator, _alternate ? TextEditCommand.End : TextEditCommand.Start);
        _document.DrainChanges();
        return _document.CaptureSnapshot().Selection;
    }

    /// <summary>Captures an immutable published snapshot.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    public TextDocumentSnapshot Snapshot() => _document.CaptureSnapshot();

    /// <summary>Generates and drains a finite batch of 32 distinct changes.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    public int Changes()
    {
        for (int i = 0; i < 32; i++)
        {
            using TextDocumentTransaction edit = _document.BeginEdit(TextEditOrigin.Keyboard);
            edit.Replace(new TextSpan(0, 1), i % 2 == 0 ? "x" : "y", new TextSelection(1, 1));
            edit.Commit();
        }

        return _document.DrainChanges().Count;
    }

    private sealed class LogicalNavigator : ITextDocumentNavigator
    {
        public TextSelection Navigate(TextDocumentSnapshot document, TextNavigation direction, bool extend)
        {
            int position = direction == TextNavigation.End ? document.Text.Length : 0;
            return new TextSelection(extend ? document.Selection.Anchor : position, position);
        }
    }
}
