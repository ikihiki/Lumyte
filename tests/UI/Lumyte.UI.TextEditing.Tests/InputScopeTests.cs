using Xunit;

namespace Lumyte.UI.TextEditing.Tests;

/// <summary>Checks surface-wide text input registrations release their borrowed handlers.</summary>
public sealed class InputScopeTests
{
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
        Assert.True(surface.TryFocus(editor));
        Assert.Equal(1, first.Calls);
        registration.Dispose();
        registration.Dispose();
        Assert.True(surface.TryFocus(null));
        Assert.Equal(1, first.Calls);
        using IDisposable replacement = UiTextInputScope.Attach(surface, second);
        Assert.True(surface.TryFocus(editor));
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
        Assert.True(firstSurface.TryFocus(editor));
        firstSurface.Dispose();
        int previousCalls = first.Calls;
        using var secondSurface = new UiSurface(editor);
        secondSurface.SetViewport(new(100, 100));
        secondSurface.UpdateLayout();
        var second = new Handler();
        using IDisposable newRegistration = UiTextInputScope.Attach(secondSurface, second);
        Assert.True(secondSurface.TryFocus(editor));
        oldRegistration.Dispose();
        Assert.True(secondSurface.TryFocus(null));
        Assert.Equal(previousCalls, first.Calls);
        Assert.Equal(2, second.Calls);
    }

    private sealed class Handler : IUiTextInputHandler
    {
        internal int Calls { get; private set; }

        public UiInputResult HandleInput(Compose.Definitions.TextBox editor, in UiInputContext context)
        {
            Calls++;
            return UiInputResult.Unhandled;
        }

        public void OnPointerCaptureChanged(Compose.Definitions.TextBox editor, UiPointerCaptureState state)
        {
        }
    }
}
