using BenchmarkDotNet.Attributes;
using Lumyte.Input.Abstractions;
using Lumyte.TextEditing;

namespace Lumyte.UI.TextEditing.Benchmarks;

/// <summary>Public UI connection CPU protocol waves; no physical OS IME is measured.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Screening", "Comparison", "Diagnostic")]
public class TextInputBenchmarks
{
    private TextInputFixture _fixture = null!;
    private bool _alternate;

    /// <summary>Creates the connected control outside measurement.</summary>
    [GlobalSetup]
    public void Setup() => _fixture = new();

    /// <summary>Performs preedit, commit, another preedit and cancellation, with callback delivery.</summary>
    /// <returns>The committed callback count in this wave.</returns>
    [Benchmark]
    public int CompositionCommitCancel()
    {
        _fixture.ResetCounters();
        TextDocument document = _fixture.Editor.Document;
        _alternate = !_alternate;
        Preedit(document, _alternate ? "かな" : "漢字");
        _fixture.Pump();
        using (TextDocumentTransaction edit = document.BeginEdit(TextEditOrigin.Platform))
        {
            edit.EndComposition(TextCompositionEnd.Commit);
            edit.Commit();
        }

        _fixture.Pump();
        Preedit(document, "取消");
        _fixture.Pump();
        using (TextDocumentTransaction edit = document.BeginEdit(TextEditOrigin.Platform))
        {
            edit.EndComposition(TextCompositionEnd.Cancel);
            edit.Commit();
        }

        _fixture.Pump();
        return _fixture.TextEvents;
    }

    /// <summary>Completes a pending clipboard read only after a conflicting revision, proving rejection.</summary>
    /// <returns>The revision after rejecting stale paste.</returns>
    [Benchmark]
    public ulong StaleAsyncPaste()
    {
        _fixture.ResetCounters();
        TextDocument document = _fixture.Editor.Document;
        _fixture.Surface.Dispatch(new UiKeyInput(Key.V, true, false, UiModifiers.Control));
        _fixture.Surface.Dispatch(new UiKeyInput(Key.V, false, false, UiModifiers.Control));
        _alternate = !_alternate;
        TextEditingCommands.ReplaceAll(document, _alternate ? "left" : "right", TextEditOrigin.Keyboard);
        _fixture.Clipboard.Complete("must-not-appear");
        _fixture.Pump();
        return document.CaptureSnapshot().Revision;
    }

    /// <summary>Completes a valid asynchronous paste into an all-selection editor.</summary>
    /// <returns>The committed callbacks in this wave.</returns>
    [Benchmark]
    public int AsyncPaste()
    {
        _fixture.ResetCounters();
        TextDocument document = _fixture.Editor.Document;
        TextDocumentSnapshot snapshot = document.CaptureSnapshot();
        document.TrySetSelection(snapshot.Revision, new(0, snapshot.Text.Length), TextEditOrigin.Keyboard);
        _fixture.Surface.Dispatch(new UiKeyInput(Key.V, true, false, UiModifiers.Control));
        _fixture.Surface.Dispatch(new UiKeyInput(Key.V, false, false, UiModifiers.Control));
        _alternate = !_alternate;
        _fixture.Clipboard.Complete(_alternate ? "paste A" : "paste B");
        _fixture.Pump();
        return _fixture.TextEvents;
    }

    /// <summary>Runs the public automation Fill mutation and callbacks, without locator polling.</summary>
    /// <returns>The committed callbacks in this wave.</returns>
    [Benchmark]
    public int AutomationFill()
    {
        _fixture.ResetCounters();
        _alternate = !_alternate;
        _fixture.Automation.Fill(_fixture.Surface, _fixture.Editor.AutomationId, _alternate ? "fill A" : "fill B");
        _fixture.Pump();
        return _fixture.TextEvents;
    }

    /// <summary>Gets the controlled fixture for deterministic protocol proof.</summary>
    /// <returns>The connected fixture.</returns>
    public TextInputFixture GetFixture() => _fixture;

    /// <summary>Retires the connection and hosted CPU resources.</summary>
    [GlobalCleanup]
    public void Cleanup() => _fixture.Dispose();

    private static void Preedit(TextDocument document, string text)
    {
        TextDocumentSnapshot snapshot = document.CaptureSnapshot();
        using TextDocumentTransaction edit = document.BeginEdit(TextEditOrigin.Platform);
        edit.SetComposition(new(Guid.NewGuid(), new(0, snapshot.Text.Length), []));
        edit.Replace(new(0, snapshot.Text.Length), text, new(text.Length, text.Length));
        edit.Commit();
    }
}
