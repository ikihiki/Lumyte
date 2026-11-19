namespace Lumyte.UI.TextEditing;

/// <summary>Defines iui text input handler.</summary>
public interface IUiTextInputHandler
{
    /// <summary>Reports an active or retiring session that still borrows the editor.</summary>
    /// <param name="editor">The existing editor realization.</param>
    /// <returns>Whether normal asynchronous input retirement is still pending.</returns>
    bool HasPendingSession(TextBoxElement editor) => false;

    /// <summary>Handle Input.</summary>
    /// <param name = "editor">The editor.</param>
    /// <param name = "context">The context.</param>
    /// <returns>The operation result.</returns>
    UiInputResult HandleInput(TextBoxElement editor, in UiInputContext context);

    /// <summary>On Pointer Capture Changed.</summary>
    /// <param name = "editor">The editor.</param>
    /// <param name = "state">The state.</param>
    void OnPointerCaptureChanged(TextBoxElement editor, UiPointerCaptureState state);
}
