using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Text.Parley;
using Lumyte.TextEditing;
using Lumyte.UI.TextEditing.Parley;
using Xunit;

namespace Lumyte.UI.TextEditing.Tests;

/// <summary>Defines parley editing tests.</summary>
public sealed class ParleyEditingTests
{
    /// <summary>Shaping Produces Scalar Safe Grapheme Carets And Selection Geometry.</summary>
    /// <param name = "text">The text.</param>
    [Theory]
    [InlineData("office AV fi")]
    [InlineData("abc אבג def")]
    [InlineData("A😀e\u0301Z")]
    public void ShapingProducesScalarSafeGraphemeCaretsAndSelectionGeometry(string text)
    {
        using var engine = new ParleyTextEngine2D();
        using Stream font = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fonts/DejaVuSans.ttf"));
        string family = engine.AddFont(font);
        var provider = new ParleyUiTextLayoutProvider(engine, new() { FontFamily = family, FontSize = 20, });
        var document = new TextDocument(text);
        UiTextLayout layout = provider.CreateLayout(document.CaptureSnapshot(), float.PositiveInfinity);
        int[] boundaries = TextDocument.GraphemeBoundaries(text);
        Assert.All(layout.CaretStops, caret => Assert.Contains(caret.Position, boundaries));
        Assert.All(boundaries, boundary => Assert.Contains(layout.CaretStops, caret => caret.Position == boundary));
        Assert.True(layout.Size.X > 0);
        Assert.True(layout.Size.Y > 0);
        Assert.NotEmpty(layout.GetRangeBounds(new(0, text.Length)));
        foreach (UiTextCaret caret in layout.CaretStops)
        {
            Rect2D rect = layout.GetCaretBounds(caret);
            Assert.True(float.IsFinite(rect.X) && float.IsFinite(rect.Y));
            UiTextCaret hit = layout.HitTest(new Vector2(rect.X, rect.Y + (rect.Height / 2)));
            Assert.Contains(hit.Position, boundaries);
            Assert.Contains(layout.MoveCaret(caret, true).Position, boundaries);
        }
    }

    /// <summary>Text Box Uses Generic Focus And Redacts Candidate Subtrees.</summary>
    [Fact]
    public void TextBoxUsesGenericFocusAndRedactsCandidateSubtrees()
    {
        var document = new TextDocument("private value");
        var editor = new Compose.Definitions.TextBox
        {
            Text = document.CaptureSnapshot().CommittedText,
            TestId = "edit",
        };
        using var surface = new UiSurface(editor);
        surface.SetViewport(new(200, 30));
        surface.UpdateLayout();
        Assert.True(surface.TryFocus(editor));
        Assert.Same(editor, surface.FindWidget(editor.AutomationId));
        UiAutomationNode node = Assert.Single(surface.CaptureAutomationSnapshot().Nodes);
        Assert.Equal(UiRole.TextBox, node.Role);
        Assert.DoesNotContain(document.CaptureSnapshot().Text, node.Name ?? string.Empty);
        var view = new Compose.Definitions.ImeCandidateView
        {
            TestId = "secret",
            AutomationName = "secret",
            Focusable = true,
        };
        using var candidates = new UiSurface(view);
        candidates.SetViewport(new(200, 100));
        candidates.UpdateLayout();
        Assert.False(candidates.TryFocus(view));
        UiAutomationNode candidate = Assert.Single(candidates.CaptureAutomationSnapshot().Nodes);
        Assert.Null(candidate.TestId);
        Assert.Null(candidate.Name);
        Assert.Equal(UiAutomationActions.None, candidate.Actions);
    }

    /// <summary>Checks the selected clause and dotted preedit are recorded through the normal drawing system.</summary>
    [Fact]
    public void PreeditDrawsSelectedClauseAndDottedUnderline()
    {
        using var engine = new ParleyTextEngine2D();
        using Stream font = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fonts/DejaVuSans.ttf"));
        string family = engine.AddFont(font);
        var provider = new ParleyUiTextLayoutProvider(engine, new() { FontFamily = family, FontSize = 20 });
        var editor = new Compose.Definitions.TextBox { Text = "AA", LayoutProvider = provider };
        TextDocument document = editor.Document;
        using (TextDocumentTransaction edit = document.BeginEdit(TextEditOrigin.Platform))
        {
            edit.SetComposition(new(Guid.NewGuid(), new(0, 2), [new(new(0, 2), TextUnderline.Dotted, true)]));
            edit.Commit();
        }

        using var surface = new UiSurface(editor);
        surface.SetViewport(new(200, 30));
        var canvas = new Canvas2D();
        canvas.Begin();
        surface.Record(canvas);
        FillRectCommand2D[] fills = canvas.End().Commands.OfType<FillRectCommand2D>().ToArray();
        Assert.Contains(fills, command => command.Rect.Height > 1);
        Assert.True(fills.Count(command => command.Rect.Height == 1 && command.Rect.Width <= 2) >= 2);
    }
}
