using Lumyte.UI.Automation;
using Lumyte.UI.Windowing;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Application;

/// <summary>Owns the current surface, binding, input and automation for one window.</summary>
public sealed partial class UiApplicationViewSlot
{
    private readonly UiApplication _application;
    private readonly UiWindowId _window;
    private View? _current;
    private ViewHost? _viewHost;
    private bool _assigned;
    private bool _stopping;
    private ulong _revision;
    private (object Metadata, Guid SurfaceId, Guid? TargetId)? _publication;

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
            return _current?.Surface;
        }
    }

    internal UiApplicationWindowContext Context { get; set; } = null!;

    internal UiPopupOwner PopupOwner => _current?.Owner ?? throw new InvalidOperationException("The initial view is not ready.");

    internal IUiAutomationHost Automation => _current?.Automation ?? throw new InvalidOperationException("The initial view is not ready.");

    internal Lumyte.UI.Windowing.TextInput.UiTextInputConnection? Text => _current?.Text?.Connection;

    internal bool IsStopped => _current?.IsStopped ?? true;

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
        _current = new(_application, _window, root);
        _current.Binding = await _application.Windows.BindPresentationAsync(_window, _current.Surface, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_stopping, this);
        _current.Surface.UpdateLayout();
        _current.Owner = _application.Popups.RegisterOwner(_window, _current.Surface);
        _current.AttachPortals();
        _current.AttachText(false);
        if (_current.Text is { } text)
        {
            _current.MenuGuard = _application.Popups.RegisterMenuGuard(_current.Owner, text.Connection.SuspendAsync);
        }

        _application.AttachInput(this, _window);
    }

    internal IUiApplicationViewHost CreateViewHost()
    {
        _application.Owner.CheckOwner();
        if (_viewHost is not null)
        {
            return _viewHost;
        }

        if (_assigned || _stopping)
        {
            throw new InvalidOperationException("Static content and staged content cannot own the same slot.");
        }

        if (_application.Windows is not IUiWindowPresentationReplacementManager)
        {
            throw new NotSupportedException("presentation-replacement-unavailable");
        }

        _assigned = true;
        _viewHost = new(this);
        return _viewHost;
    }

    internal (ulong Revision, object? Metadata, Guid SurfaceId, Guid? TargetId) CaptureViewSnapshot()
    {
        _application.Owner.CheckOwner();
        return _publication is { } view ? (_revision, view.Metadata, view.SurfaceId, view.TargetId) : (_revision, null, default, null);
    }

    internal IUiAutomationHost GetAutomation(bool allowInspection) => _current?.GetAutomation(allowInspection) ?? throw new InvalidOperationException("The initial view is not ready.");

    internal void SynchronizeFocus() => _current?.Text?.Connection.SynchronizeFocus();

    internal void Advance(bool animationFrame)
    {
        if (_current is { } view && !_stopping)
        {
            bool visible = _application.Driver.CanAnimate && _application.State != UiApplicationState.Recovering
                && _application.Windows.TryGetSnapshot(_window, out UiWindowSnapshot? state) && state is { IsVisible: true, ClientSize.X: > 0, ClientSize.Y: > 0 };
            if (!view.Suspended)
            {
                view.Surface.SetSignalUpdatesEnabled(true);
            }

            view.Surface.SetAnimationUpdatesEnabled(visible && !view.Suspended);
            if (animationFrame || view.Surface.HasPendingAnimationUpdates)
            {
                view.Surface.AdvanceAnimations();
            }

            view.Surface.UpdateLayout();
            _application.ValidateEditors(view.Surface);
            view.Visuals.Advance();
            view.Portals?.Advance();
            if (view.Gated && !view.Suspended && !view.Surface.PageTransitioning && _application.Windows.TryGetSnapshot(_window, out UiWindowSnapshot? snapshot) && snapshot is { IsVisible: true })
            {
                view.Surface.SetSignalUpdatesEnabled(true);
                view.Surface.SetAnimationUpdatesEnabled(visible);
                view.Portals?.SetUpdatesEnabled(true);
                view.Gated = false;
                view.Host.Resume();
                _application.Popups.GateOwner(view.Owner!, false);
            }
        }
    }

    internal void BeginStop()
    {
        if (_stopping)
        {
            return;
        }

        _stopping = true;
        _publication = null;
        _revision++;
        _application.DetachInput(this);
        _current?.BeginStop();
    }

    internal async ValueTask DetachAsync()
    {
        if (_current is { } view)
        {
            await view.ReleaseOwnedAsync();
            _current = null;
        }
    }

    private sealed class View
    {
        private readonly UiApplication _application;
        private readonly UiWindowId _window;
        private Task? _closingVisuals;
        private Task? _release;
        private ReleaseStage _releaseStage;

        internal View(UiApplication application, UiWindowId window, Widget root)
        {
            _application = application;
            _window = window;
            Surface = new(root, application.Dispatcher, application.AnimationClock);
            Visuals = application.AttachVisuals(Surface, () => _window);
            Visuals.Publish();
            Host = UiApplicationAutomationHost.Create(application, () => Surface);
        }

        private enum ReleaseStage
        {
            Visuals,
            MenuGuard,
            Text,
            Automation,
            Binding,
            Surface,
            Complete,
        }

        internal UiVisualCoordinator Visuals { get; }

        internal bool Suspended { get; set; }

        internal UiSurface Surface { get; }

        internal UiWindowBinding? Binding { get; set; }

        internal UiPopupOwner? Owner { get; set; }

        internal UiApplicationAutomationHost Host { get; }

        internal UiApplication.TextSite? Text { get; private set; }

        internal UiPopupPortalCoordinator? Portals { get; private set; }

        internal IDisposable? MenuGuard { get; set; }

        internal bool Gated { get; set; }

        internal IUiAutomationHost Automation => Text?.Automation ?? Host;

        internal bool IsStopped => (_closingVisuals?.IsCompleted ?? true) && (Text?.IsStopped ?? true) && (Owner?.IsReleased ?? true) && Host.IsStopped;

        internal IUiAutomationHost GetAutomation(bool inspection) => Text?.CreateAutomation(inspection) ?? Host;

        internal void AttachPortals() => Portals = new(_application.Popups, Owner!, Surface);

        internal void AttachText(bool staged) => Text = _application.AttachText(Surface, _window, Host, staged);

        internal void BeginStop(bool animate = true)
        {
            if (!animate)
            {
                Visuals.BeginStop();
            }

            Surface.SetSignalUpdatesEnabled(false);
            Surface.FreezeValueAnimations();
            Portals?.Dispose();
            Host.BeginStop();
            Text?.BeginStop();
            Owner?.Dispose();
            Surface.Dispatch(new UiFocusLostInput());
            _closingVisuals ??= Visuals.DisposeAsync().AsTask();
        }

        internal ValueTask ReleaseOwnedAsync() => ReleaseAsync(true);

        internal ValueTask ReleaseTransferredAsync() => ReleaseAsync(false);

        private ValueTask ReleaseAsync(bool binding)
        {
            if (_release is null || _release.IsFaulted || _release.IsCanceled)
            {
                _release = ReleaseCoreAsync(binding);
            }

            return new(_release);
        }

        private async Task ReleaseCoreAsync(bool binding)
        {
            if (_releaseStage == ReleaseStage.Visuals)
            {
                if (_closingVisuals is not null)
                {
                    await _closingVisuals;
                }

                await Visuals.DrainAsync();
                _releaseStage = ReleaseStage.MenuGuard;
            }

            if (_releaseStage == ReleaseStage.MenuGuard)
            {
                MenuGuard?.Dispose();
                MenuGuard = null;
                _releaseStage = ReleaseStage.Text;
            }

            if (_releaseStage == ReleaseStage.Text)
            {
                if (Text is not null)
                {
                    await Text.DisposeAsync();
                    Text = null;
                }

                _releaseStage = ReleaseStage.Automation;
            }

            if (_releaseStage == ReleaseStage.Automation)
            {
                Host.Dispose();
                _releaseStage = ReleaseStage.Binding;
            }

            if (_releaseStage == ReleaseStage.Binding)
            {
                if (binding && Binding is not null)
                {
                    await Binding.DisposeAsync();
                }

                Binding = null;
                _releaseStage = ReleaseStage.Surface;
            }

            if (_releaseStage == ReleaseStage.Surface)
            {
                _application.ForgetRetiredSurface(Surface);
                Surface.Dispose();
                _releaseStage = ReleaseStage.Complete;
            }
        }
    }
}
