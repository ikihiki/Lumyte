using Lumyte.Signals;
using Lumyte.TextEditing;
using Xunit;

namespace Lumyte.UI.TextEditing.Tests;

/// <summary>Checks surface-wide text input registrations release their borrowed handlers.</summary>
public sealed class InputScopeTests
{
    /// <summary>Checks a signal-backed controlled declaration preserves the implementation-owned document and composition.</summary>
    [Fact]
    public void SignalReconstructionKeepsTheEditingDocument()
    {
        var text = Signal.Create("hello");
        Compose.Definitions.TextBox first = Compose.TextBox(text: text, key: "editor");
        using var surface = new UiSurface(first);
        var element = (TextBoxElement)surface.RootElement;
        TextDocument document = element.Document;
        using (TextDocumentTransaction edit = document.BeginEdit(TextEditOrigin.Platform))
        {
            edit.SetComposition(new(Guid.NewGuid(), new(0, 0), []));
            edit.Replace(new(0, 0), "かな", new(2, 2));
            edit.Commit();
        }

        text.Value = "external";
        surface.UpdateTree();
        Compose.Definitions.TextBox current = Assert.IsType<Compose.Definitions.TextBox>(surface.Root);
        Assert.NotSame(first, current);
        Assert.Same(element, surface.RootElement);
        Assert.Same(document, element.Document);
        Assert.NotNull(element.Document.CaptureSnapshot().Composition);
        Assert.Equal("external", current.Text);
    }

    /// <summary>Checks duplicate registration, detachment and replacement without assigning any editor handler.</summary>
    [Fact]
    public void RegistrationIsUniqueAndCanBeReplacedAfterDisposal()
    {
        var editor = new Compose.Definitions.TextBox { Width = 20, Height = 20 };
        using var surface = new UiSurface(editor);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        var first = new Handler();
        var second = new Handler();
        IDisposable registration = UiTextInputScope.Attach(surface, first);
        Assert.Throws<ArgumentException>(() => UiTextInputScope.Attach(surface, second));
        Assert.True(surface.TryFocus(surface.RootElement));
        Assert.Equal(1, first.Calls);
        registration.Dispose();
        registration.Dispose();
        Assert.True(surface.TryFocus(null));
        Assert.Equal(1, first.Calls);
        using IDisposable replacement = UiTextInputScope.Attach(surface, second);
        Assert.True(surface.TryFocus(surface.RootElement));
        Assert.Equal(1, first.Calls);
        Assert.Equal(1, second.Calls);
    }

    /// <summary>Checks a reconnected widget resolves its new surface even while the old registration still exists.</summary>
    [Fact]
    public void ReconnectedEditorDoesNotRetainItsPreviousSurfaceHandler()
    {
        var editor = new Compose.Definitions.TextBox { Width = 20, Height = 20 };
        var firstSurface = new UiSurface(editor);
        firstSurface.SetViewport(new(100, 100));
        firstSurface.UpdateLayout();
        var first = new Handler();
        using IDisposable oldRegistration = UiTextInputScope.Attach(firstSurface, first);
        Assert.True(firstSurface.TryFocus(firstSurface.RootElement));
        firstSurface.Dispose();
        int previousCalls = first.Calls;
        using var secondSurface = new UiSurface(editor);
        secondSurface.SetViewport(new(100, 100));
        secondSurface.UpdateLayout();
        var second = new Handler();
        using IDisposable newRegistration = UiTextInputScope.Attach(secondSurface, second);
        Assert.True(secondSurface.TryFocus(secondSurface.RootElement));
        oldRegistration.Dispose();
        Assert.True(secondSurface.TryFocus(null));
        Assert.Equal(previousCalls, first.Calls);
        Assert.Equal(2, second.Calls);
    }

    /// <summary>Checks independent mounts own independent documents and compatible rebuilds retain the editing instance.</summary>
    [Fact]
    public void DocumentsBelongToElementsRatherThanDeclarations()
    {
        Compose.Definitions.TextBox declaration = Compose.TextBox(text: "initial");
        using var first = new UiSurface(declaration);
        using var second = new UiSurface(declaration);
        first.SetViewport(new(100, 100));
        second.SetViewport(new(100, 100));
        first.UpdateLayout();
        second.UpdateLayout();
        var instance = (TextBoxElement)first.RootElement;
        var other = (TextBoxElement)second.RootElement;
        Assert.NotSame(instance.Document, other.Document);
        Lumyte.TextEditing.TextDocument document = instance.Document;
        Assert.True(document.TryReplace(document.CaptureSnapshot().Revision, new(0, 7), "edited", new(6, 6), Lumyte.TextEditing.TextEditOrigin.Keyboard));
        Assert.Equal("initial", other.Document.CaptureSnapshot().CommittedText);
        Assert.Equal("initial", declaration.Text);
        first.Rebuild(Compose.TextBox(text: "accepted"));
        first.UpdateLayout();
        Assert.Same(instance, first.RootElement);
        Assert.Same(document, instance.Document);
        Assert.Equal("accepted", document.CaptureSnapshot().CommittedText);
    }

    private sealed class Handler : IUiTextInputHandler
    {
        internal int Calls { get; private set; }

        public UiInputResult HandleInput(TextBoxElement editor, in UiInputContext context)
        {
            Calls++;
            return UiInputResult.Unhandled;
        }

        public void OnPointerCaptureChanged(TextBoxElement editor, UiPointerCaptureState state)
        {
        }
    }
}
