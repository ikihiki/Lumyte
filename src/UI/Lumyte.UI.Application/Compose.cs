using System.Numerics;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Application;

/// <summary>Creates passive application window declarations with ordinary static calls.</summary>
public static class Compose
{
    /// <summary>Declares a window; no operating-system or DI content is created here.</summary>
    /// <param name="key">A stable ordinal key.</param>
    /// <param name="title">The window title.</param>
    /// <param name="root">The root factory, exclusive with content.</param>
    /// <param name="content">The owned content factory.</param>
    /// <param name="isOpen">The model's requested open state.</param>
    /// <param name="owner">An optional declaration key.</param>
    /// <param name="initialClientSize">The initial size, preserved through later declarations.</param>
    /// <param name="presentation">The native or hosted policy.</param>
    /// <param name="onCloseRequested">Requests a model update without closing automatically.</param>
    /// <param name="onOpened">Reports a new instance.</param>
    /// <param name="onClosed">Reports a finished instance.</param>
    /// <returns>The immutable declaration.</returns>
    public static UiWindowDeclaration Window(string key, string title, Func<UiApplicationWindowContext, Widget>? root = null, Func<IServiceProvider, IUiApplicationContent>? content = null, bool isOpen = true, string? owner = null, Vector2? initialClientSize = null, WindowPresentation presentation = WindowPresentation.Automatic, Action<UiWindowCloseRequest>? onCloseRequested = null, Action<UiApplicationWindowResult>? onOpened = null, Action<UiApplicationWindowResult>? onClosed = null) =>
        new(key, title, root, content, isOpen, owner, initialClientSize ?? new(800, 600), presentation, onCloseRequested, onOpened, onClosed);
}
