using Lumyte.TextEditing;
using Xunit;

namespace Lumyte.UI.TextEditing.Tests;

/// <summary>Defines text document tests.</summary>
public sealed class TextDocumentTests
{
    /// <summary>Transactions Publish Atomically And Rollback On Dispose.</summary>
    [Fact]
    public void TransactionsPublishAtomicallyAndRollbackOnDispose()
    {
        var document = new TextDocument("before");
        using (TextDocumentTransaction edit = document.BeginEdit(TextEditOrigin.Platform))
        {
            edit.Replace(new(0, 6), "after", new(5, 5));
            Assert.Equal("before", document.CaptureSnapshot().Text);
            Assert.Throws<InvalidOperationException>(() => document.BeginEdit(TextEditOrigin.Application));
        }

        Assert.Equal("before", document.CaptureSnapshot().Text);
        Assert.Empty(document.DrainChanges());
        Assert.True(document.TryReplace(0, new(0, 6), "after", new(5, 5), TextEditOrigin.Application));
        Assert.Equal(1UL, document.CaptureSnapshot().Revision);
        Assert.True(Assert.Single(document.DrainChanges()).CommittedTextChanged);
        Assert.False(document.TryReplace(0, new(0, 5), "stale", new(5, 5), TextEditOrigin.Application));
        Assert.Equal("after", document.CaptureSnapshot().Text);
    }

    /// <summary>Preedit Replaces And Cancel Restores Checkpoint.</summary>
    [Fact]
    public void PreeditReplacesAndCancelRestoresCheckpoint()
    {
        var document = new TextDocument("A");
        var id = Guid.NewGuid();
        using (TextDocumentTransaction edit = document.BeginEdit(TextEditOrigin.Platform))
        {
            edit.SetComposition(new(id, new(1, 0), []));
            edit.Replace(new(1, 0), "かな", new(3, 3));
            edit.Commit();
        }

        Assert.Equal("Aかな", document.CaptureSnapshot().Text);
        Assert.Equal("A", document.CaptureSnapshot().CommittedText);
        using (TextDocumentTransaction edit = document.BeginEdit(TextEditOrigin.Platform))
        {
            edit.Replace(new(1, 2), "仮名", new(3, 3));
            edit.EndComposition(TextCompositionEnd.Cancel);
            edit.Commit();
        }

        Assert.Equal("A", document.CaptureSnapshot().Text);
        Assert.Equal(new TextSelection(1, 1), document.CaptureSnapshot().Selection);
        Assert.Null(document.CaptureSnapshot().Composition);
    }

    /// <summary>Partial Commit Survives Cancellation Of Remaining Preedit.</summary>
    [Fact]
    public void PartialCommitSurvivesCancellationOfRemainingPreedit()
    {
        var document = new TextDocument();
        var id = Guid.NewGuid();
        using (TextDocumentTransaction edit = document.BeginEdit(TextEditOrigin.Platform))
        {
            edit.SetComposition(new(id, new(0, 0), []));
            edit.Replace(new(0, 0), "日本語", new(3, 3));
            edit.Commit();
        }

        using (TextDocumentTransaction edit = document.BeginEdit(TextEditOrigin.Platform))
        {
            edit.SetComposition(new(id, new(2, 1), []));
            edit.Commit();
        }

        Assert.Equal("日本", document.CaptureSnapshot().CommittedText);
        using (TextDocumentTransaction edit = document.BeginEdit(TextEditOrigin.Platform))
        {
            edit.EndComposition(TextCompositionEnd.Cancel);
            edit.Commit();
        }

        Assert.Equal("日本", document.CaptureSnapshot().Text);
    }

    /// <summary>Normalization Preserves Unicode Without Canonical Conversion.</summary>
    /// <param name = "input">The input.</param>
    /// <param name = "expected">The expected.</param>
    [Theory]
    [InlineData("a\r\nb\rc\nd", "a b c d")]
    [InlineData("😀e\u0301", "😀e\u0301")]
    public void NormalizationPreservesUnicodeWithoutCanonicalConversion(string input, string expected) => Assert.Equal(expected, new TextDocument(input).CaptureSnapshot().Text);

    /// <summary>Malformed Unicode Is Rejected.</summary>
    /// <param name = "codeUnit">The invalid UTF-16 code unit.</param>
    [Theory]
    [InlineData(0xD800)]
    [InlineData(0xDC00)]
    public void MalformedUnicodeIsRejected(int codeUnit) => Assert.Throws<ArgumentException>(() => new TextDocument(new string((char)codeUnit, 1)));

    /// <summary>Limits And Scalar Boundaries Fail Before Publication.</summary>
    [Fact]
    public void LimitsAndScalarBoundariesFailBeforePublication()
    {
        var document = new TextDocument("😀", 4);
        Assert.Throws<ArgumentOutOfRangeException>(() => document.TryReplace(0, new(1, 0), "x", new(2, 2), TextEditOrigin.Application));
        Assert.Throws<ArgumentException>(() => document.TryReplace(0, new(0, 0), "long", new(4, 4), TextEditOrigin.Application));
        Assert.Equal("😀", document.CaptureSnapshot().Text);
        Assert.Equal(0UL, document.CaptureSnapshot().Revision);
    }

    /// <summary>Backspace Deletes One Extended Grapheme.</summary>
    /// <param name = "grapheme">The grapheme.</param>
    [Theory]
    [InlineData("e\u0301")]
    [InlineData("👨‍👩‍👧‍👦")]
    [InlineData("🇯🇵")]
    public void BackspaceDeletesOneExtendedGrapheme(string grapheme)
    {
        var document = new TextDocument("A" + grapheme);
        Assert.True(TextEditingCommands.Execute(document, new LogicalNavigator(), TextEditCommand.DeleteBackward));
        Assert.Equal("A", document.CaptureSnapshot().Text);
    }

    /// <summary>Composition Snapshots Do Not Retain Caller Mutable Lists.</summary>
    [Fact]
    public void CompositionSnapshotsDoNotRetainCallerMutableLists()
    {
        var document = new TextDocument("abc");
        var spans = new List<TextCompositionSpan>
        {
            new(new(0, 3), TextUnderline.Solid, true),
        };
        using (TextDocumentTransaction edit = document.BeginEdit(TextEditOrigin.Platform))
        {
            edit.SetComposition(new(Guid.NewGuid(), new(0, 3), spans));
            edit.Commit();
        }

        spans.Clear();
        Assert.Single(document.CaptureSnapshot().Composition!.Spans);
    }

    private sealed class LogicalNavigator : ITextDocumentNavigator
    {
        /// <summary>Navigate.</summary>
        /// <param name = "document">The document.</param>
        /// <param name = "direction">The direction.</param>
        /// <param name = "extend">The extend.</param>
        /// <returns>The operation result.</returns>
        public TextSelection Navigate(TextDocumentSnapshot document, TextNavigation direction, bool extend) => document.Selection;
    }
}
