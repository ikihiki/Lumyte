using Lumyte.UI.Automation;
using Lumyte.UI.TextEditing;
using Lumyte.UI.Windowing;

namespace Lumyte.UI.Application;

/// <summary>Provides borrowed per-window services for one non-reusable instance lifetime.</summary>
public sealed class UiApplicationWindowContext
{
    private readonly UiApplication _application;
    private bool _closed;

    internal UiApplicationWindowContext(UiApplication application, UiWindowId window, UiApplicationViewSlot view, IUiTextLayoutProvider? text)
    {
        _application = application;
        Window = window;
        View = view;
        TextLayoutProvider = text;
    }

    /// <summary>Gets the unique instance identity.</summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>Gets the logical window identity.</summary>
    public UiWindowId Window { get; }

    /// <summary>Gets the application-owned window manager.</summary>
    public IUiWindowManager Windows => _application.Windows;

    /// <summary>Gets the shared dispatcher.</summary>
    public IUiDispatcher Dispatcher => _application.Dispatcher;

    /// <summary>Gets the window's view slot.</summary>
    public UiApplicationViewSlot View { get; }

    /// <summary>Gets the borrowed text layout provider.</summary>
    public IUiTextLayoutProvider? TextLayoutProvider { get; }

    /// <summary>Gets the root service provider; create independent scopes explicitly.</summary>
    public IServiceProvider Services => _application.Services;

    /// <summary>Gets automation through the same queue, frame and input domain.</summary>
    public IUiAutomationHost Automation => View.Automation;

    /// <summary>Gets the popup coordinator for this application's windows.</summary>
    public UiPopupManager Popups => _application.Popups;

    /// <summary>Gets the borrowed popup owner; its lifetime is managed by the application.</summary>
    public UiPopupOwner PopupOwner => View.PopupOwner;

    /// <summary>Gets the optional hosted title bar and resize grip.</summary>
    public UiApplicationWindowChrome? Chrome { get; internal set; }

    /// <summary>Requests orderly application shutdown from a callback.</summary>
    public void RequestStop() => _application.RequestStop();

    /// <summary>Gets the current popup host on the UI owner, sharing the application's input reservation.</summary>
    /// <param name="popupId">An open popup identity.</param>
    /// <returns>The borrowed popup host.</returns>
    public IUiAutomationHost GetPopupAutomation(Guid popupId) => _application.PopupAutomation(popupId);

    internal IUiAutomationHost GetPopupAutomationForDiagnostics(Guid popup, bool allowInspection) => _application.PopupAutomation(popup, allowInspection);

    internal void Close() => _closed = true;

    internal void CheckOwner()
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        _application.Owner.CheckOwner();
    }
}
