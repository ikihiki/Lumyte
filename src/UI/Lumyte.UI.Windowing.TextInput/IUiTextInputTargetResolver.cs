using Lumyte.Platform.TextInput.Abstractions;

namespace Lumyte.UI.Windowing.TextInput;

/// <summary>Defines iui text input target resolver.</summary>
public interface IUiTextInputTargetResolver
{
    /// <summary>Resolve.</summary>
    /// <param name = "focusWindow">The focus window.</param>
    /// <param name = "presentationWindow">The presentation window.</param>
    /// <param name = "surfaceId">The surface id.</param>
    /// <param name = "editorId">The editor id.</param>
    /// <returns>The operation result.</returns>
    TextInputTarget Resolve(UiWindowId focusWindow, UiWindowId presentationWindow, Guid surfaceId, Guid editorId);
}
