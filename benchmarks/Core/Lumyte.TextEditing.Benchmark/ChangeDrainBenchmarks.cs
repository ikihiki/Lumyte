using BenchmarkDotNet.Attributes;

namespace Lumyte.TextEditing.Benchmark;

/// <summary>Separates destructive drain from untimed replenishment of real changes.</summary>
public class ChangeDrainBenchmarks
{
    private TextDocument _document = null!;

    /// <summary>Gets or sets finite change-batch size.</summary>
    [Params(1, 32, 256)]
    public int Count { get; set; }

    /// <summary>Replenishes a fresh document and distinct committed changes before each invocation.</summary>
    [IterationSetup]
    public void Setup()
    {
        _document = new TextDocument(new string('a', 4096));
        for (int index = 0; index < Count; index++)
        {
            using TextDocumentTransaction edit = _document.BeginEdit(TextEditOrigin.Keyboard);
            edit.Replace(new TextSpan(0, 1), index % 2 == 0 ? "x" : "y", new TextSelection(1, 1));
            edit.Commit();
        }
    }

    /// <summary>Drains a nonempty batch once; setup prevents subsequent empty-drain measurements.</summary>
    /// <returns>The complete published change batch.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-TEXT-05")]
    public IReadOnlyList<TextDocumentChange> Drain() => _document.DrainChanges();
}
