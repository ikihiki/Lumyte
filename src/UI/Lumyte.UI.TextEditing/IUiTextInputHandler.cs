namespace Lumyte.UI.TextEditing;

/// <summary>Defines iui text input handler.</summary>
public interface IUiTextInputHandler
{
    /// <summary>Handle Input.</summary>
    /// <param name = "editor">The editor.</param>
    /// <param name = "context">The context.</param>
    /// <returns>The operation result.</returns>
    UiInputResult HandleInput(Compose.Definitions.TextBox editor, in UiInputContext context);

    /// <summary>On Pointer Capture Changed.</summary>
    /// <param name = "editor">The editor.</param>
    /// <param name = "state">The state.</param>
    void OnPointerCaptureChanged(Compose.Definitions.TextBox editor, UiPointerCaptureState state);
}
