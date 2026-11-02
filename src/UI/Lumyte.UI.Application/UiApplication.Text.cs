using System.Runtime.CompilerServices;
using Lumyte.UI.Automation;
using Lumyte.UI.TextEditing.Automation;
using Lumyte.UI.Windowing;
using Lumyte.UI.Windowing.TextInput;
using TextBox = Lumyte.UI.TextEditing.TextBoxElement;

namespace Lumyte.UI.Application;

/// <summary>Owns the UI loop and resources from startup through ordered shutdown.</summary>
public sealed partial class UiApplication
{
    private readonly List<TextSite> _textSites = [];
    private readonly ConditionalWeakTable<UiSurface, EditorValidation> _validatedEditors = new();
    private readonly Dictionary<Guid, PopupSite> _popupHosts = [];

    internal TextSite? AttachText(UiSurface surface, UiWindowId window, UiApplicationAutomationHost automation)
    {
        if (Driver.TextInput is not { } service)
        {
            return null;
        }

        IUiTextInputTargetResolver targets = Driver.CreateTextTargets(this);
        var connection = new UiTextInputConnection(surface, Windows, window, service, targets, Driver.Clipboard);
        UiImeCandidateController? candidates = TextLayoutProvider is { } provider ? new(connection, new(Windows, provider), Popups, _options.ImeCandidates, clock: TimeProvider) : null;
        var site = new TextSite(this, surface, window, connection, candidates, automation);
        _textSites.Add(site);
        return site;
    }

    internal IUiAutomationHost PopupAutomation(Guid id, bool allowInspection = false)
    {
        Owner.CheckOwner();
        DiscoverPopupText();
        return _popupHosts.TryGetValue(id, out PopupSite? site) ? site.GetAutomation(allowInspection) : throw new InvalidOperationException("popup-unavailable");
    }

    internal void ValidateEditors(Lumyte.UI.Compose.Definitions.Widget root)
    {
        var pending = new Stack<Lumyte.UI.Compose.Definitions.Widget>();
        pending.Push(root);
        while (pending.TryPop(out Lumyte.UI.Compose.Definitions.Widget? declaration))
        {
            ValidateEditor(declaration);
            foreach (WidgetChildren slot in declaration.ChildCollections)
            {
                foreach (Lumyte.UI.Compose.Definitions.Widget child in slot)
                {
                    pending.Push(child);
                }
            }
        }
    }

    internal void ValidateEditors(UiSurface surface)
    {
        if (_validatedEditors.TryGetValue(surface, out EditorValidation? validation) && validation.Revision == surface.HostValidationRevision)
        {
            return;
        }

        foreach (UiElement element in surface.HostValidationElements)
        {
            ValidateEditor(element.Widget);
        }

        validation = _validatedEditors.GetValue(surface, static _ => new());
        validation.Revision = surface.HostValidationRevision;
    }

    private void ValidateEditor(Lumyte.UI.Compose.Definitions.Widget declaration)
    {
        if (declaration is not Lumyte.UI.TextEditing.Compose.Definitions.TextBox editor)
        {
            return;
        }

        if (editor.LayoutProvider is null)
        {
            throw new UiApplicationException("invalid-editor");
        }

        if (!editor.IsReadOnly && (TextLayoutProvider is null || Driver.TextInput is null))
        {
            throw new UiApplicationException("text-input-unavailable");
        }
    }

    private void DiscoverPopupText()
    {
        if (_popups is null)
        {
            return;
        }

        foreach (UiPopupInfo popup in Popups.CapturePopups())
        {
            if (_popupHosts.ContainsKey(popup.Id) || !Popups.TryGetSurface(popup.Id, out UiSurface? surface) || surface is null || Popups.FindWindow(surface) is not { } window)
            {
                continue;
            }

            var automation = UiApplicationAutomationHost.Create(this, () => surface);
            TextSite? text = AttachText(surface, window, automation);
            var site = new PopupSite(this, popup.Id, surface, automation, text);
            _popupHosts.Add(popup.Id, site);
            Popups.BindLifetime(popup.Id, site);
        }
    }

    private void AdvanceText()
    {
        SynchronizeFocus();
        foreach (TextSite site in _textSites.ToArray())
        {
            site.Advance();
        }

        SynchronizeFocus();
    }

    private void SynchronizeTextFocus()
    {
        TextSite? desired = State is UiApplicationState.Recovering or UiApplicationState.Stopping or UiApplicationState.StopFailed ? null : _textSites.FirstOrDefault(site => !site.Stopping && site.IsActive && Popups.CanReceiveKeys(site.Surface) && site.Surface.FocusedElement is TextBox { Widget: { IsReadOnly: false, IsEnabled: true, IsVisible: true } });
        foreach (TextSite site in _textSites)
        {
            if (!ReferenceEquals(site, desired))
            {
                site.Pause();
            }
        }

        if (_textSites.All(site => ReferenceEquals(site, desired) || site.Paused))
        {
            desired?.Activate();
        }

        foreach (TextSite site in _textSites)
        {
            site.Connection.SynchronizeFocus();
        }
    }

    internal sealed class PopupSite(UiApplication application, Guid id, UiSurface surface, UiApplicationAutomationHost host, TextSite? text) : IAsyncDisposable
    {
        private bool _stopping;

        internal UiSurface Surface { get; } = surface;

        internal IUiAutomationHost Automation => text?.Automation ?? host;

        public async ValueTask DisposeAsync()
        {
            if (!_stopping)
            {
                _stopping = true;
                host.BeginStop();
                text?.BeginStop();
            }

            if (text is not null)
            {
                await text.DisposeAsync();
            }

            while (!host.IsStopped)
            {
                await application.NextTurnAsync();
            }

            host.Dispose();
            application._popupHosts.Remove(id);
        }

        internal IUiAutomationHost GetAutomation(bool allowInspection) => text?.CreateAutomation(allowInspection) ?? host;
    }

    internal sealed class TextSite : IAsyncDisposable
    {
        private readonly UiApplication _application;
        private readonly UiImeCandidateController? _candidates;
        private readonly UiApplicationAutomationHost _automation;
        private Task<IAsyncDisposable>? _pause;
        private bool _disposed;

        internal TextSite(UiApplication application, UiSurface surface, UiWindowId window, UiTextInputConnection connection, UiImeCandidateController? candidates, UiApplicationAutomationHost automation)
        {
            _application = application;
            Surface = surface;
            Window = window;
            Connection = connection;
            _candidates = candidates;
            _automation = automation;
            Automation = new UiTextAutomationHost(automation, connection, candidates);
            Pause();
        }

        internal UiSurface Surface { get; }

        internal UiWindowId Window { get; }

        internal bool IsActive => _application.Windows.TryGetSnapshot(Window, out UiWindowSnapshot? snapshot) && snapshot is { IsVisible: true, State: UiWindowState.Open }
            && (snapshot.IsActive || (_application.Windows.TryGetSnapshot(snapshot.KeyboardRoot, out UiWindowSnapshot? keyboard) && keyboard is { IsActive: true, State: UiWindowState.Open }));

        internal UiTextInputConnection Connection { get; }

        internal IUiAutomationHost Automation { get; }

        internal bool Stopping { get; private set; }

        internal bool Paused => _pause?.IsCompletedSuccessfully == true;

        internal bool IsStopped => Stopping && Connection.IsStopped && (_candidates?.IsStopped ?? true) && _automation.IsStopped;

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            BeginStop();
            while (!IsStopped)
            {
                await _application.NextTurnAsync();
            }

            _candidates?.Dispose();
            Connection.Dispose();
            _application._textSites.Remove(this);
            _disposed = true;
        }

        internal IUiAutomationHost CreateAutomation(bool allowInspection) => new UiTextAutomationHost(_automation, Connection, _candidates, allowInspection);

        internal void Pause()
        {
            if (!Stopping && _pause is null)
            {
                _pause = Connection.SuspendAsync().AsTask();
            }
        }

        internal void Activate()
        {
            if (!Stopping && _pause is { IsCompletedSuccessfully: true })
            {
                ReleasePause();
            }
        }

        internal void Advance()
        {
            if (Stopping && _pause?.IsCompleted == true)
            {
                ReleasePause();
            }

            Connection.Advance();
            _candidates?.Advance();
            if (!Stopping)
            {
                Surface.UpdateLayout();
                Connection.PublishGeometry();
                _candidates?.PublishGeometry();
            }
        }

        internal void BeginStop()
        {
            if (Stopping)
            {
                return;
            }

            Stopping = true;
            _automation.BeginStop();
            _candidates?.BeginStop();
            Connection.BeginStop();
            if (_pause?.IsCompleted == true)
            {
                ReleasePause();
            }
        }

        private void ReleasePause()
        {
            Task<IAsyncDisposable>? pause = _pause;
            _pause = null;
            if (pause?.IsCompletedSuccessfully == true)
            {
                ValueTask release = pause.Result.DisposeAsync();
                if (!release.IsCompletedSuccessfully)
                {
                    throw new InvalidOperationException("The text suspension token must release synchronously.");
                }

                release.GetAwaiter().GetResult();
            }
        }
    }

    private sealed class EditorValidation
    {
        internal ulong Revision { get; set; }
    }
}
