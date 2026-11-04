using Lumyte.Graphics.Text.Parley;
using Lumyte.TextEditing;
using Lumyte.UI.TextEditing.Parley;
using Xunit;

namespace Lumyte.UI.TextEditing.Tests;

/// <summary>Checks retained local text content against real immutable Parley outlines.</summary>
public sealed class RetainedTextDrawingTests
{
    /// <summary>Checks a document edit recaptures only its editor, not another editor's glyph content.</summary>
    [Fact]
    public void DocumentRevisionUpdatesOnlyOneEditorFragment()
    {
        using var engine = new ParleyTextEngine2D();
        using Stream font = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fonts/DejaVuSans.ttf"));
        string family = engine.AddFont(font);
        var provider = new ParleyUiTextLayoutProvider(engine, new() { FontFamily = family, FontSize = 20 });
        var first = new Compose.Definitions.TextBox { Text = "first", LayoutProvider = provider, Width = 100, Height = 30 };
        var second = new Compose.Definitions.TextBox { Text = "second", LayoutProvider = provider, Width = 100, Height = 30 };
        using var surface = new UiSurface(Lumyte.UI.Compose.Panel()[first, second]);
        surface.SetViewport(new(200, 60));
        using UiDrawingScene2D drawing = surface.CreateDrawingScene();
        drawing.Update();
        Assert.Equal(3, drawing.LastRecordingCount);
        drawing.Update();
        Assert.Equal(0, drawing.LastRecordingCount);
        var editor = (TextBoxElement)surface.RootElement.Children[0];
        var untouched = (TextBoxElement)surface.RootElement.Children[1];
        UiTextLayout? original = untouched.CurrentLayout;
        TextDocument document = editor.Document;
        TextDocumentSnapshot snapshot = document.CaptureSnapshot();
        Assert.True(document.TryReplace(snapshot.Revision, new(0, snapshot.Text.Length), "updated", new(7, 7), TextEditOrigin.Keyboard));
        drawing.Update();
        Assert.Equal(2, drawing.LastRecordingCount);
        Assert.Same(original, untouched.CurrentLayout);
        Assert.Equal(document.CaptureSnapshot().Revision, editor.CurrentLayout!.DocumentRevision);
        drawing.Update();
        Assert.Equal(0, drawing.LastRecordingCount);
    }

    /// <summary>Checks focus-only caret changes invalidate local retained drawing.</summary>
    [Fact]
    public void FocusChangeInvalidatesCaretRecording()
    {
        using var surface = new UiSurface(new Compose.Definitions.TextBox());
        surface.SetViewport(new(100, 30));
        using UiDrawingScene2D drawing = surface.CreateDrawingScene();
        drawing.Update();
        drawing.Update();
        Assert.Equal(0, drawing.LastRecordingCount);
        Assert.True(surface.TryFocus(surface.RootElement));
        drawing.Update();
        Assert.Equal(1, drawing.LastRecordingCount);
    }
}
