using Lumyte.Input.Abstractions;
using Lumyte.Platform.TextInput.Abstractions;
using Lumyte.TextEditing;
using Lumyte.UI.TextEditing;
using Lumyte.UI.TextEditing.Benchmarks;
using Lumyte.UI.TextEditing.Parley.Benchmarks;
using Xunit;

namespace Lumyte.UI.Benchmark.Tests;

/// <summary>Functional proof of bounded editor waves and native CPU UI metadata.</summary>
public sealed class TextEditingBenchmarkTests
{
    /// <summary>Checks real construction, insert/delete, caret changes and keyed reconciliation.</summary>
    /// <param name="length">The bounded source length.</param>
    [Theory]
    [InlineData(32)]
    [InlineData(512)]
    public void EditorWavesRetainDocumentAndAdvanceState(int length)
    {
        var benchmark = new TextBoxBenchmarks { Length = length };
        benchmark.Setup();
        try
        {
            TextBoxElement editor = benchmark.GetEditor();
            TextDocument document = editor.Document;
            Assert.Equal(length, benchmark.Construct());
            Assert.Equal(2, benchmark.InsertDelete());
            Assert.Equal(new string('a', length), document.CaptureSnapshot().Text);
            Assert.Equal(length, benchmark.SelectionCaret());
            Assert.Equal(length / 2, benchmark.SelectionCaret());
            ulong before = document.CaptureSnapshot().Revision;
            Assert.True(benchmark.ReconcileExternalString() > before);
            Assert.Same(editor, benchmark.GetEditor());
            Assert.Same(document, benchmark.GetEditor().Document);
            Assert.Equal(length / 2, document.CaptureSnapshot().Selection.Active);
            Assert.Equal(length + 1, document.CaptureSnapshot().Text.Length);
            benchmark.ReconcileExternalString();
            Assert.Equal(length, document.CaptureSnapshot().Text.Length);
        }
        finally
        {
            benchmark.Cleanup();
        }
    }

    /// <summary>Checks composition commit/cancel revision, callbacks, geometry and per-wave reset.</summary>
    [Fact]
    public void CompositionWavesPublishCurrentRevisionAndDrain()
    {
        var benchmark = new TextInputBenchmarks();
        benchmark.Setup();
        try
        {
            TextInputFixture fixture = benchmark.GetFixture();
            Assert.True(fixture.Session.Status.IsActive);
            Assert.Equal(1, benchmark.CompositionCommitCancel());
            Assert.Equal(4, fixture.CompositionEvents);
            TextDocumentSnapshot first = fixture.Editor.Document.CaptureSnapshot();
            Assert.Equal("かな", first.CommittedText);
            Assert.Equal(first.CommittedText, first.Text);
            Assert.Null(first.Composition);
            Assert.Equal(new TextSelection(2, 2), first.Selection);
            Assert.Equal(first.Revision, fixture.Connection.Geometry!.DocumentRevision);
            Assert.Equal(1, benchmark.CompositionCommitCancel());
            Assert.Equal(4, fixture.CompositionEvents);
            Assert.Equal("漢字", fixture.Editor.Document.CaptureSnapshot().CommittedText);
            Assert.True(fixture.Editor.Document.CaptureSnapshot().Revision > first.Revision);
        }
        finally
        {
            benchmark.Cleanup();
        }
    }

    /// <summary>Checks session-generation key routing and public release after cleanup.</summary>
    [Fact]
    public void ConnectionRejectsStaleKeysAndReleasesSession()
    {
        var benchmark = new TextInputBenchmarks();
        benchmark.Setup();
        TextInputFixture fixture = benchmark.GetFixture();
        TextInputSessionBase session = fixture.Session;
        TextInputSessionStatus status = session.Status;
        Assert.False(fixture.Connection.DispatchKey(new(default, default, default, Guid.NewGuid(), status.Generation, 1, Key.End, true, false, false, false, false, false)));
        Assert.False(fixture.Connection.DispatchKey(new(default, default, default, status.SessionId, status.Generation + 1, 1, Key.End, true, false, false, false, false, false)));
        benchmark.Cleanup();
        Assert.True(session.IsReleased);
        Assert.True(fixture.Connection.IsStopped);
    }

    /// <summary>Checks successful asynchronous paste and revision-stale clipboard rejection.</summary>
    [Fact]
    public void PasteWavesAcceptCurrentAndRejectStaleRevision()
    {
        var benchmark = new TextInputBenchmarks();
        benchmark.Setup();
        try
        {
            TextInputFixture fixture = benchmark.GetFixture();
            Assert.Equal(1, benchmark.AsyncPaste());
            Assert.Equal("paste A", fixture.Editor.Document.CaptureSnapshot().Text);
            Assert.Equal(1, benchmark.AsyncPaste());
            Assert.Equal("paste B", fixture.Editor.Document.CaptureSnapshot().Text);
            benchmark.StaleAsyncPaste();
            Assert.Equal("left", fixture.Editor.Document.CaptureSnapshot().Text);
            benchmark.StaleAsyncPaste();
            Assert.Equal("right", fixture.Editor.Document.CaptureSnapshot().Text);
            Assert.True(fixture.Connection.IsInputIdle);
            Assert.Equal(1, benchmark.AutomationFill());
            Assert.Equal("fill A", fixture.Editor.Document.CaptureSnapshot().Text);
            Assert.Equal(1, benchmark.AutomationFill());
            Assert.Equal("fill B", fixture.Editor.Document.CaptureSnapshot().Text);
        }
        finally
        {
            benchmark.Cleanup();
        }
    }

    /// <summary>Checks explicit fonts, wrapping, grapheme-safe UI carets and revision metadata.</summary>
    /// <param name="script">The native CPU script fixture.</param>
    [Theory]
    [InlineData("Latin")]
    [InlineData("Japanese")]
    [InlineData("Bidi")]
    [InlineData("Grapheme")]
    public void ParleyLayoutsExposeActualUiEditingGeometry(string script)
    {
        var benchmark = new ParleyUiLayoutBenchmarks { Script = script };
        benchmark.Setup();
        try
        {
            UiTextLayout warm = benchmark.WarmWrappedLayout();
            UiTextLayout cold = benchmark.ColdEngineLayout();
            Assert.Equal(warm.Size, cold.Size);
            Assert.True(warm.Size.X > 0);
            Assert.True(warm.Size.Y > 18);
            Assert.NotEmpty(warm.GetRangeBounds(new(0, 8)));
            Assert.All(warm.CaretStops, c => Assert.Contains(c.Position, TextDocument.GraphemeBoundaries(benchmark.CaptureDocument().Text)));
            ulong before = benchmark.CaptureDocument().Revision;
            UiTextLayout edited = benchmark.EditMetadata();
            Assert.True(edited.DocumentRevision > before);
            Assert.Equal(benchmark.CaptureDocument().Revision, edited.DocumentRevision);
            Assert.True(benchmark.EditMetadata().DocumentRevision > edited.DocumentRevision);
        }
        finally
        {
            benchmark.Cleanup();
        }
    }
}
