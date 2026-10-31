using Lumyte.UI.Automation;
using Lumyte.UI.Windowing;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Application;

/// <summary>Owns the current surface, binding, input and automation for one window.</summary>
public sealed class UiApplicationViewSlot
{
    private readonly UiApplication _application;
    private readonly UiWindowId _window;
    private UiSurface? _surface;
    private UiWindowBinding? _binding;
    private UiPopupOwner? _popupOwner;
    private UiApplicationAutomationHost? _automation;
    private UiApplication.TextSite? _text;
    private IDisposable? _menuGuard;
    private bool _assigned;
    private bool _stopping;

    internal UiApplicationViewSlot(UiApplication application, UiWindowId window)
    {
        _application = application;
        _window = window;
    }

    /// <summary>Gets the borrowed current surface, or null after release.</summary>
    public UiSurface? CurrentSurface
    {
        get
        {
            _application.Owner.CheckOwner();
            return _surface;
        }
    }

    internal UiPopupOwner PopupOwner => _popupOwner ?? throw new InvalidOperationException("The initial view is not ready.");

    internal IUiAutomationHost Automation => _text?.Automation ?? _automation ?? throw new InvalidOperationException("The initial view is not ready.");

    internal Lumyte.UI.Windowing.TextInput.UiTextInputConnection? Text => _text?.Connection;

    internal bool IsStopped => (_text?.IsStopped ?? true) && (_popupOwner?.IsReleased ?? true) && (_automation?.IsStopped ?? true);

    /// <summary>Publishes the initial disconnected tree exactly once.</summary>
    /// <param name="root">The declaration root.</param>
    /// <param name="cancellationToken">Requests startup cancellation.</param>
    /// <returns>Binding and layout completion.</returns>
    public async ValueTask SetInitialRootAsync(Widget root, CancellationToken cancellationToken = default)
    {
        _application.Owner.CheckOwner();
        ArgumentNullException.ThrowIfNull(root);
        if (_assigned || _stopping)
        {
            throw new InvalidOperationException("The slot is already initialized or stopping.");
        }

        _application.ValidateEditors(root);
        _assigned = true;
        _surface = new(root);
        _binding = await _application.Windows.BindPresentationAsync(_window, _surface, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_stopping, this);
        _surface.UpdateLayout();
        _popupOwner = _application.Popups.RegisterOwner(_window, _surface);
        _automation = UiApplicationAutomationHost.Create(_application, () => _surface);
        _text = _application.AttachText(_surface, _window, _automation);
        if (_text is not null)
        {
            _menuGuard = _application.Popups.RegisterMenuGuard(_popupOwner, _text.Connection.SuspendAsync);
        }

        _application.AttachInput(this, _window);
    }

    internal IUiAutomationHost GetAutomation(bool allowInspection) => _text?.CreateAutomation(allowInspection) ?? Automation;

    internal void SynchronizeFocus() => _text?.Connection.SynchronizeFocus();

    internal void Advance()
    {
        if (_surface is not null && !_stopping)
        {
            _application.ValidateEditors(_surface.Root);
            _surface.UpdateLayout();
        }
    }

    internal void BeginStop()
    {
        if (_stopping)
        {
            return;
        }

        _stopping = true;
        _application.DetachInput(this);
        _automation?.BeginStop();
        _text?.BeginStop();
        _popupOwner?.Dispose();
        _surface?.Dispatch(new UiFocusLostInput());
    }

    internal async ValueTask DetachAsync()
    {
        _menuGuard?.Dispose();
        _menuGuard = null;
        if (_text is not null)
        {
            await _text.DisposeAsync();
        }

        _text = null;
        _automation?.Dispose();
        if (_binding is not null)
        {
            await _binding.DisposeAsync();
            _binding = null;
        }

        _surface?.Dispose();
        _surface = null;
    }
}
