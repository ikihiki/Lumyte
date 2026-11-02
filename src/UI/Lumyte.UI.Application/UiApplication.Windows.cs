using Lumyte.Graphics.Drawing2D;
using Lumyte.Input;
using Lumyte.Input.Abstractions;
using Lumyte.UI.Windowing;
using Lumyte.Windowing.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.UI.Application;

/// <summary>Owns the UI loop and resources from startup through ordered shutdown.</summary>
public sealed partial class UiApplication
{
    private readonly Dictionary<UiSurface, UiFocusBookmark> _inactiveFocus = [];

    internal IWindow? ResolveNative(Guid source) => Driver.ResolveNative(source) ?? _entries.Values.Select(entry => entry.Root).Distinct().FirstOrDefault(window => window.Source.SourceId == source)?.NativeWindow;

    internal void AttachInput(UiApplicationViewSlot slot, UiWindowId window)
    {
        if (_router is not null && Windows.TryGetSnapshot(window, out UiWindowSnapshot? snapshot) && snapshot is not null && ResolveNative(snapshot.SourceId) is { } native)
        {
            // A native source needs one acquisition anchor; PopupManager selects all hosted destinations.
            if (!_entries.Values.Any(entry => entry.InputAttached && entry.Root.NativeWindow == native))
            {
                _router.Attach(native, slot.CurrentSurface!);
                _entries.Values.Single(entry => ReferenceEquals(entry.Slot, slot)).InputAttached = true;
            }
        }
    }

    internal void DetachInput(UiApplicationViewSlot slot)
    {
        if (slot.CurrentSurface is { } surface)
        {
            _router?.Detach(surface);
        }
    }

    internal void AttachAuxInput(UiDriverWindow window, UiSurface surface)
    {
        _auxInput.Add(window, surface);
        _router?.Attach(window.NativeWindow!, surface);
    }

    internal void DetachAuxInput(UiDriverWindow window)
    {
        if (_auxInput.Remove(window, out UiSurface? surface))
        {
            _router?.Detach(surface);
        }
    }

    internal void NotifyTargetLost(UiDriverWindow window)
    {
        if (!_entries.TryGetValue(window.Key, out Entry? entry) || entry.Stopping)
        {
            return;
        }

        if (window.Key == _primaryKey)
        {
            if (!Driver.IsQuitRequested)
            {
                RecordFailure(new UiApplicationException("primary-target-lost"));
            }

            RequestStop();
        }
        else if (_reconciling is null)
        {
            entry.Error = new UiApplicationException("window-target-lost");
            _failedKeys.Add(window.Key);
            _reconciling = _dispatcher.TrackAsync(() => CloseEntryAsync(entry));
        }
    }

    private bool DispatchPhysicalInput(InputRecord input)
    {
        if (input.Data is FocusData focus && input.StateWindowId is { } origin)
        {
            foreach (Entry entry in _entries.Values.Where(entry => !entry.Stopping))
            {
                if (entry.Slot.CurrentSurface is not { } surface || !Windows.TryGetSnapshot(entry.Context.Window, out UiWindowSnapshot? snapshot) || snapshot?.InputOrigin != origin)
                {
                    continue;
                }

                if (!focus.IsFocused)
                {
                    _inactiveFocus.TryAdd(surface, surface.CaptureFocus());
                }
                else if (_inactiveFocus.Remove(surface, out UiFocusBookmark? bookmark) && surface.FocusedElement is null)
                {
                    surface.TryRestoreFocus(bookmark);
                }
            }
        }

        return _router!.Dispatch(input);
    }

    private async Task StartAsync()
    {
        await Driver.InitializeAsync(this, _services, _textFactory is not null, _startupCancellation.Token);
        await PrepareExecutionAsync();
        _startupCancellation.Token.ThrowIfCancellationRequested();
        UiWindowDeclaration[] declarations = BuildDeclarations();
        UiWindowDeclaration primary = declarations.Single(window => window.Key == _primaryKey);
        _primary = await Driver.CreateWindowAsync(primary.Key, primary.Title, primary.InitialClientSize, null, true, _startupCancellation.Token);
        ConfigureSource(_primary);
        _defaultWindows = Driver.CreateWindowManager(_options.TimeProvider);
        _windows = _managerFactory?.Invoke(new(_defaultWindows, _primary.PresentationSource, _dispatcher, _options.TimeProvider)) ?? _defaultWindows;
        if (_windows is null)
        {
            throw new UiApplicationException("invalid-window-manager");
        }

        _popups = new(_windows, _options.TimeProvider);
        TextLayoutProvider = _textFactory?.Invoke(_services) ?? (_textFactory is not null ? throw new UiApplicationException("invalid-editor") : null);
        if (Driver.InputSource is { } source)
        {
            _router = new(source, source, (origin, device, input) => !HasInputLease && Popups.Dispatch(new(origin, input, device)));
            Driver.DispatchInput = DispatchPhysicalInput;
        }

        Driver.CloseRequested = window =>
        {
            if (!_entries.TryGetValue(window.Key, out Entry? entry) || entry.Stopping)
            {
                return;
            }

            Action<UiWindowCloseRequest>? callback = entry.Declaration.OnCloseRequested;
            if (callback is not null)
            {
                callback(new(window.Key, UiWindowCloseReason.User));
            }
            else if (window.Key == _primaryKey)
            {
                RequestStop();
            }
        };
        Driver.SynchronizeFocus = SynchronizeFocus;
        foreach (Func<IServiceProvider, IUiApplicationParticipant> factory in _participantFactories)
        {
            IUiApplicationParticipant participant = factory(_services);
            _participants.Add(participant);
            await participant.StartAsync(_startupCancellation.Token);
        }

        _resources = _services.GetService<Lumyte.Resources.ResourceManager>();
        await CreateEntryAsync(primary, null, true);
        await ReconcileDeclarationsAsync(declarations, true);
    }

    private UiWindowDeclaration[] BuildDeclarations()
    {
        UiWindowDeclaration[] declarations = (_buildWindows(new(this)) ?? throw new UiApplicationException("invalid-window-list")).ToArray();
        var keys = new Dictionary<string, UiWindowDeclaration>(StringComparer.Ordinal);
        foreach (UiWindowDeclaration declaration in declarations)
        {
            if (declaration is null || !keys.TryAdd(declaration.Key, declaration))
            {
                throw new UiApplicationException("invalid-window-list");
            }
        }

        if (!keys.TryGetValue(_primaryKey, out UiWindowDeclaration? primary) || !primary.IsOpen || primary.Owner is not null || primary.Presentation != WindowPresentation.Automatic)
        {
            throw new UiApplicationException("invalid-primary-window");
        }

        foreach (UiWindowDeclaration declaration in declarations)
        {
            var path = new HashSet<string>(StringComparer.Ordinal) { declaration.Key };
            for (UiWindowDeclaration current = declaration; current.Owner is { } owner;)
            {
                if (!path.Add(owner) || !keys.TryGetValue(owner, out UiWindowDeclaration? parent) || (declaration.IsOpen && !parent.IsOpen))
                {
                    throw new UiApplicationException("invalid-window-owner");
                }

                current = parent;
            }
        }

        return declarations;
    }

    private Task ReconcileAsync(bool initial) => ReconcileDeclarationsAsync(BuildDeclarations(), initial);

    private async Task ReconcileDeclarationsAsync(UiWindowDeclaration[] declarations, bool initial)
    {
        Dictionary<string, UiWindowDeclaration> desired = declarations.ToDictionary(window => window.Key, StringComparer.Ordinal);
        var replacing = _entries.Values.Where(entry => !desired.TryGetValue(entry.Declaration.Key, out UiWindowDeclaration? next) || !next.IsOpen
            || !entry.Opened || next.Owner != entry.Declaration.Owner || next.Presentation != entry.Declaration.Presentation
            || (next.Root is null) != (entry.Declaration.Root is null)).Select(entry => entry.Declaration.Key).ToHashSet(StringComparer.Ordinal);
        foreach (Entry entry in _entries.Values.OrderBy(Depth).ToArray())
        {
            if (EffectiveOwner(entry) is { } owner && replacing.Contains(owner))
            {
                replacing.Add(entry.Declaration.Key);
            }
        }

        foreach (Entry entry in _entries.Values.OrderByDescending(Depth).ToArray())
        {
            if (replacing.Contains(entry.Declaration.Key))
            {
                if (EffectiveOwner(entry) is { } owner && replacing.Contains(owner))
                {
                    entry.Declaration.OnCloseRequested?.Invoke(new(entry.Declaration.Key, UiWindowCloseReason.OwnerClosing));
                }

                await CloseEntryAsync(entry, entry.Declaration.Key == _primaryKey);
            }
            else
            {
                UiWindowDeclaration next = desired[entry.Declaration.Key];
                entry.Declaration = next;
                entry.Native?.SetTitle(next.Title);
                entry.Context.Chrome?.UpdateTitle(next.Title);
            }
        }

        _failedKeys.RemoveWhere(key => !desired.TryGetValue(key, out UiWindowDeclaration? value) || !value.IsOpen);
        if (!_entries.ContainsKey(_primaryKey))
        {
            await CreateEntryAsync(desired[_primaryKey], null, true);
        }

        var pending = declarations.Where(window => window.IsOpen && !_entries.ContainsKey(window.Key) && !_failedKeys.Contains(window.Key)).ToList();
        while (pending.Count != 0)
        {
            bool progressed = false;
            foreach (UiWindowDeclaration declaration in pending.ToArray())
            {
                string ownerKey = declaration.Owner ?? _primaryKey;
                if (!_entries.TryGetValue(ownerKey, out Entry? owner))
                {
                    continue;
                }

                pending.Remove(declaration);
                progressed = true;
                try
                {
                    await CreateEntryAsync(declaration, owner, false);
                }
                catch (Exception error)
                {
                    _failedKeys.Add(declaration.Key);
                    if (_entries.TryGetValue(declaration.Key, out Entry? failed))
                    {
                        failed.Error = error;
                        await CloseEntryAsync(failed);
                    }
                    else
                    {
                        declaration.OnClosed?.Invoke(new(declaration.Key, Guid.NewGuid(), error is UiApplicationException failure ? failure.Code : "content-initialize-failed", error));
                    }
                }
            }

            if (!progressed)
            {
                foreach (UiWindowDeclaration declaration in pending)
                {
                    _failedKeys.Add(declaration.Key);
                    declaration.OnClosed?.Invoke(new(declaration.Key, Guid.NewGuid(), "owner-unavailable"));
                }

                break;
            }
        }
    }

    private async Task CreateEntryAsync(UiWindowDeclaration declaration, Entry? owner, bool primary)
    {
        _startupCancellation.Token.ThrowIfCancellationRequested();
        UiDriverWindow? native;
        bool hosted = !primary && declaration.Presentation switch
        {
            WindowPresentation.Hosted => true,
            WindowPresentation.Automatic => !Driver.NativeWindows || (declaration.Owner is not null && owner?.Native is null),
            WindowPresentation.NativeRequired => false,
            _ => throw new UiApplicationException("unsupported-presentation"),
        };
        if (!primary && declaration.Presentation == WindowPresentation.NativeRequired && (!Driver.NativeWindows || (declaration.Owner is not null && owner?.Native is null)))
        {
            throw new UiApplicationException("native-unavailable");
        }

        if (hosted)
        {
            native = null;
        }
        else
        {
            try
            {
                native = primary ? _primary : await Driver.CreateWindowAsync(declaration.Key, declaration.Title, declaration.InitialClientSize, declaration.Owner is not null ? owner?.Native : null, false, _startupCancellation.Token);
            }
            catch (Exception error) when (!primary && declaration.Presentation == WindowPresentation.Automatic && error is not OperationCanceledException && error is not UiApplicationException { Code: "window-cleanup-failed" })
            {
                hosted = true;
                native = null;
            }
        }

        if (native is not null)
        {
            native.Source.ValidateRoot = ValidateEditors;
            native.Source.ValidateSurface = ValidateEditors;
        }

        UiWindowRegistration? registration = null;
        UiWindowId id;
        try
        {
            registration = native is not null ? Windows.Register(native.PresentationSource) : null;
            id = registration?.Window ?? Windows.Create(new(owner!.Context.Window, declaration.InitialClientSize, UiWindowRole.Normal, UiWindowInputMode.Interactive, new(owner.Context.Window, new Rect2D(16, 16, 0, 0), UiWindowPlacementSide.Below), UiWindowPolicy.HostedOnly));
        }
        catch
        {
            if (native is not null && !primary)
            {
                await Driver.CloseWindowAsync(native);
            }

            throw;
        }

        var slot = new UiApplicationViewSlot(this, id);
        var context = new UiApplicationWindowContext(this, id, slot, TextLayoutProvider);
        slot.Context = context;
        var entry = new Entry(declaration, context, slot, native, registration, native ?? owner!.Root);
        _entries.Add(declaration.Key, entry);
        ConfigureSource(entry.Root);
        entry.Content = declaration.Root is { } root ? new UiRootApplicationContent(root) : declaration.Content!(_services) ?? throw new UiApplicationException("invalid-content");
        entry.Initialization = entry.Content.InitializeAsync(context, _startupCancellation.Token).AsTask();
        await entry.Initialization;
        _startupCancellation.Token.ThrowIfCancellationRequested();
        if (slot.CurrentSurface is null)
        {
            throw new UiApplicationException("invalid-content");
        }

        if (!primary && Volatile.Read(ref _rebuildRequested) != 0)
        {
            return;
        }

        if (hosted)
        {
            context.Chrome = new(this, context, declaration.Title, () =>
            {
                Windows.SetKeyboardTarget(context.Chrome!.HeaderWindow);
                entry.Declaration.OnCloseRequested?.Invoke(new(entry.Declaration.Key, UiWindowCloseReason.User));
            });
            await context.Chrome.PrepareAsync(declaration.InitialClientSize);
            UiWindowRequestId show = Windows.Show(id);
            context.Chrome.Show();
            while (!Windows.TryGetRequest(show, out UiWindowRequestResult? result) || result?.Status == UiWindowRequestStatus.Pending)
            {
                await NextTurnAsync();
                _startupCancellation.Token.ThrowIfCancellationRequested();
            }

            if (!Windows.TryGetRequest(show, out UiWindowRequestResult? shown) || shown?.Status != UiWindowRequestStatus.Succeeded)
            {
                throw new UiApplicationException(shown?.Code ?? "window-show-failed");
            }
        }
        else
        {
            native!.Show();
        }

        entry.Opened = true;
        foreach (IUiApplicationParticipant participant in _participants)
        {
            participant.OpenWindow(context);
        }

        declaration.OnOpened?.Invoke(new(declaration.Key, context.Id, "opened", Presentation: hosted ? UiWindowPresentation.Hosted : UiWindowPresentation.External));
    }

    private string? EffectiveOwner(Entry entry) => entry.Declaration.Owner ?? (entry.Declaration.Key == _primaryKey ? null : _primaryKey);

    private int Depth(Entry entry)
    {
        int depth = 0;
        for (string? owner = EffectiveOwner(entry); owner is not null && _entries.TryGetValue(owner, out Entry? parent); owner = EffectiveOwner(parent))
        {
            depth++;
        }

        return depth;
    }

    private void SynchronizeFocus() => SynchronizeTextFocus();

    private sealed class Entry(UiWindowDeclaration declaration, UiApplicationWindowContext context, UiApplicationViewSlot slot,
        UiDriverWindow? native, UiWindowRegistration? registration, UiDriverWindow root)
    {
        internal UiWindowDeclaration Declaration { get; set; } = declaration;

        internal UiApplicationWindowContext Context { get; } = context;

        internal UiApplicationViewSlot Slot { get; } = slot;

        internal UiDriverWindow? Native { get; } = native;

        internal UiDriverWindow Root { get; } = root;

        internal UiWindowRegistration? Registration { get; } = registration;

        internal IUiApplicationContent? Content { get; set; }

        internal Task? Initialization { get; set; }

        internal Task<bool>? Drawing { get; set; }

        internal Exception? Error { get; set; }

        internal bool InputAttached { get; set; }

        internal bool Opened { get; set; }

        internal bool Stopping { get; set; }

        internal bool Retired { get; set; }

        internal bool Detached { get; set; }

        internal bool Released { get; set; }
    }
}
