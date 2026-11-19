using System.Numerics;
using Lumyte.UI.Windowing;

namespace Lumyte.UI.Controls.Windowing;

/// <summary>Creates ordinary declarative dialog portals without an imperative opening service.</summary>
public static class DialogCompose
{
    /// <summary>Declares controlled dialog content using the existing Windowing owner loop.</summary>
    /// <param name="key">The stable sibling identity.</param>
    /// <param name="title">The explicit semantic title.</param>
    /// <param name="isOpen">The authoritative model intent.</param>
    /// <param name="isModal">Whether to shield this logical owner; defaults to true.</param>
    /// <param name="presentation">The explicit presentation policy.</param>
    /// <param name="closeOnEscape">Whether Escape requests closure; defaults to true.</param>
    /// <param name="closeOnOutsidePress">Whether outside modal presses request closure.</param>
    /// <param name="initialClientSize">The positive finite opening size, defaulting to 320 by 200.</param>
    /// <param name="initialFocus">An optional content key.</param>
    /// <param name="onCloseRequested">Requests closure without writing model state.</param>
    /// <param name="onOpened">Notifies actual presentation.</param>
    /// <param name="onClosed">Notifies complete resource retirement or native capability failure.</param>
    /// <returns>A zero-sized portal declaration with a single-assignment content indexer.</returns>
    public static Lumyte.UI.Windowing.Compose.Definitions.Dialog Dialog(
        string key,
        UiArgument<string> title,
        UiArgument<bool> isOpen = default,
        UiArgument<bool> isModal = default,
        UiArgument<DialogPresentation> presentation = default,
        UiArgument<bool> closeOnEscape = default,
        UiArgument<bool> closeOnOutsidePress = default,
        Vector2? initialClientSize = null,
        string? initialFocus = null,
        Action<UiDialogCloseRequest>? onCloseRequested = null,
        Action<UiDialogOpened>? onOpened = null,
        Action<UiDialogClosed>? onClosed = null) =>
        Lumyte.UI.Windowing.Compose.Dialog(key: key, title: title, isOpen: isOpen, isModal: isModal, presentation: presentation, closeOnEscape: closeOnEscape, closeOnOutsidePress: closeOnOutsidePress, initialClientSize: initialClientSize ?? new(320, 200), initialFocus: initialFocus, onCloseRequested: onCloseRequested, onOpened: onOpened, onClosed: onClosed);
}
