using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Signals;
using Lumyte.TextEditing;
using Xunit;
using static Lumyte.UI.Compose;
using static Lumyte.UI.TextEditing.Compose;

namespace Lumyte.UI.TextEditing.Tests;

/// <summary>Exercises chrome without replacing the editing owner.</summary>
public sealed class VisualSlotTests
{
    /// <summary>Concrete factories expose chrome and exclude it from editor semantics and input.</summary>
    /// <param name="numeric">Whether to exercise the derived concrete factory.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BackgroundDoesNotOwnInputOrMeasurement(bool numeric)
    {
        int clicks = 0;
        Lumyte.UI.Compose.Definitions.Button decoration = Button(testId: "decoration", onClick: () => clicks++, width: 900, height: 900);
        Lumyte.UI.Compose.Definitions.Widget declaration = numeric
            ? NumericBox(value: 5, width: 100, height: 30)[NumericBox.Background()[decoration]]
            : TextBox(text: "text", width: 100, height: 30)[TextBox.Background()[decoration]];
        using var surface = new UiSurface(declaration);
        surface.SetViewport(new(100, 30));
        surface.UpdateLayout();
        TextBoxElement editor = Assert.IsAssignableFrom<TextBoxElement>(surface.RootElement);
        UiElement background = Assert.Single(editor.Children);
        Assert.Equal(new Vector2(100, 30), editor.Size);
        Assert.Equal(editor.ContentSize, background.Size);
        Assert.False(surface.TryFocus(background));
        surface.Dispatch(new UiPointerInput(0, UiPointerPhase.Down, new(5, 5)));
        surface.Dispatch(new UiPointerInput(0, UiPointerPhase.Up, new(5, 5)));
        Assert.Same(editor, surface.FocusedElement);
        Assert.Equal(0, clicks);
        Assert.Single(surface.CaptureAutomationSnapshot().Nodes);
        Assert.True(surface.MoveFocus(UiFocusNavigation.First));
        Assert.Same(editor, surface.FocusedElement);
        using var intrinsic = new UiSurface(Panel()[TextBox()[TextBox.Background()[Panel(width: 900, height: 900)]]]);
        intrinsic.SetViewport(new(1000));
        intrinsic.UpdateLayout();
        Assert.Equal(Vector2.Zero, intrinsic.RootElement.Children[0].Size);
    }

    /// <summary>Opaque chrome draws even without a layout provider and empty chrome is transparent.</summary>
    [Fact]
    public void BackgroundIsActualChromeAndEmptyIsTransparent()
    {
        using var surface = new UiSurface(TextBox(width: 100, height: 30));
        surface.SetViewport(new(100, 30));
        Assert.Equal(0, Record(surface).Count);
        surface.Rebuild(TextBox(width: 100, height: 30)[TextBox.Background()[Rectangle(fill: new Lumyte.Mathematics.Colors.SRgba<float>(1, 0, 0, 1))]]);
        surface.UpdateTree();
        Assert.Equal(1, Record(surface).Count);
        surface.Rebuild(TextBox(width: 100, height: 30)[TextBox.Background()[Array.Empty<Lumyte.UI.Compose.Definitions.Widget>()]]);
        surface.UpdateTree();
        Assert.Equal(0, Record(surface).Count);
    }

    /// <summary>Chrome rebuilds preserve the editing document, selection and preedit across independent surfaces.</summary>
    [Fact]
    public void RebuildPreservesDocumentSelectionAndCompositionAndIsAtomic()
    {
        Compose.Definitions.TextBox declaration = TextBox(text: "text", width: 100, height: 30)[TextBox.Background()[Rectangle(fill: new Lumyte.Mathematics.Colors.SRgba<float>(1, 0, 0, 1))]];
        using var first = new UiSurface(declaration);
        using var second = new UiSurface(declaration);
        first.SetViewport(new(100, 30));
        second.SetViewport(new(200, 60));
        first.UpdateLayout();
        second.UpdateLayout();
        var editor = (TextBoxElement)first.RootElement;
        TextDocument document = editor.Document;
        Assert.NotSame(document, ((TextBoxElement)second.RootElement).Document);
        Assert.NotSame(editor.Children[0], second.RootElement.Children[0]);
        using (TextDocumentTransaction edit = document.BeginEdit(TextEditOrigin.Platform))
        {
            edit.SetComposition(new(Guid.NewGuid(), new(0, 1), []));
            edit.Replace(new(0, 1), "preedit", new(3, 3));
            edit.Commit();
        }

        TextDocumentSnapshot before = document.CaptureSnapshot();
        first.Rebuild(TextBox(text: "text", width: 100, height: 30)[TextBox.Background()[Panel()]]);
        first.UpdateTree();
        first.UpdateLayout();
        Assert.Same(editor, first.RootElement);
        Assert.Same(document, editor.Document);
        Assert.Equal(before.Selection, document.CaptureSnapshot().Selection);
        Assert.Equal(before.Composition, document.CaptureSnapshot().Composition);
        UiElement background = Assert.Single(editor.Children);
        first.Rebuild(TextBox(text: "changed")[TextBox.Background()[new InvalidPart()]]);
        Assert.Throws<ArgumentOutOfRangeException>(first.UpdateTree);
        Assert.Same(background, editor.Children[0]);
        Assert.Equal(before.Composition, document.CaptureSnapshot().Composition);
        Assert.Equal("text", ((TextBoxElement)second.RootElement).Document.CaptureSnapshot().CommittedText);
    }

    /// <summary>Chrome is recorded before editor-owned text, selection and caret drawing.</summary>
    [Fact]
    public void BackgroundRecordsBeforeEditorDrawing()
    {
        var order = new List<string>();
        var declaration = new OrderedEditor { Order = order, Width = 100, Height = 30 };
        declaration.BackgroundChildren.Initialize(new OrderedBackground { Order = order });
        using var surface = new UiSurface(declaration);
        surface.SetViewport(new(100, 30));
        Record(surface);
        Assert.Equal(new[] { "background", "editor" }, order);
    }

    /// <summary>Mounted background remains visible and correctly ordered after declaration-only reactive rebuilds.</summary>
    [Fact]
    public void RepeatedTextSignalsKeepMountedChrome()
    {
        var text = Signal.Create("first");
        using var surface = new UiSurface(TextBox(text: text, width: 100, height: 30)[TextBox.Background()[Rectangle(fill: new Lumyte.Mathematics.Colors.SRgba<float>(1, 0, 0, 1))]]);
        surface.SetViewport(new(100, 30));
        surface.UpdateLayout();
        var editor = (TextBoxElement)surface.RootElement;
        UiElement background = Assert.Single(editor.Children);
        Assert.Equal(1, Record(surface).Count);
        text.Value = "second";
        surface.UpdateTree();
        Assert.Equal(1, Record(surface).Count);
        Assert.Same(background, Assert.Single(editor.Children));
        text.Value = "third";
        surface.UpdateTree();
        Assert.Equal(1, Record(surface).Count);
        Assert.Same(background, Assert.Single(editor.Children));
        Assert.Equal("third", editor.Document.CaptureSnapshot().CommittedText);
    }

    /// <summary>Direct BackgroundChildren initialization obeys the same empty and retained drawing rules.</summary>
    /// <param name="empty">Whether the background is intentionally empty.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectBackgroundInitializationSurvivesSignals(bool empty)
    {
        var text = Signal.Create("first");
        Compose.Definitions.TextBox declaration = TextBox(text: text, width: 100, height: 30);
        declaration.BackgroundChildren.Initialize(empty ? [] : [Rectangle(fill: new Lumyte.Mathematics.Colors.SRgba<float>(1, 0, 0, 1))]);
        using var surface = new UiSurface(declaration);
        surface.SetViewport(new(100, 30));
        Assert.Equal(empty ? 0 : 1, Record(surface).Count);
        text.Value = "second";
        surface.UpdateTree();
        Assert.Equal(empty ? 0 : 1, Record(surface).Count);
        text.Value = "third";
        surface.UpdateTree();
        Assert.Equal(empty ? 0 : 1, Record(surface).Count);
        Assert.Equal("third", ((TextBoxElement)surface.RootElement).Document.CaptureSnapshot().CommittedText);
    }

    private static DrawList2D Record(UiSurface surface)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        surface.Record(canvas);
        return canvas.End();
    }

    private sealed class OrderedEditor : Compose.Definitions.TextBox
    {
        public required List<string> Order { get; init; }

        protected override UiElement CreateElement() => new OrderedEditorElement(this);
    }

    private sealed class OrderedEditorElement(OrderedEditor widget) : TextBoxElement(widget)
    {
        protected override void OnRecord(DrawingCanvas2D canvas)
        {
            widget.Order.Add("editor");
            base.OnRecord(canvas);
        }
    }

    private sealed class OrderedBackground : Lumyte.UI.Compose.Definitions.Panel
    {
        public required List<string> Order { get; init; }

        protected override UiElement CreateElement() => new OrderedBackgroundElement(this);
    }

    private sealed class OrderedBackgroundElement(OrderedBackground widget) : UiElement<OrderedBackground>(widget)
    {
        protected override void OnRecord(DrawingCanvas2D canvas) => Widget.Order.Add("background");
    }

    private sealed class InvalidPart : Lumyte.UI.Compose.Definitions.Panel
    {
        protected override void ValidateDeclaration(UiWidgetValidationContext context) => throw new ArgumentOutOfRangeException(nameof(context));
    }
}
