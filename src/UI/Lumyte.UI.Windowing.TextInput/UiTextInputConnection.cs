using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Input.Abstractions;
using Lumyte.Platform.Abstractions;
using Lumyte.Platform.TextInput.Abstractions;
using Lumyte.TextEditing;
using Lumyte.UI.TextEditing;
using Lumyte.Windowing.Abstractions;
using TextBox = Lumyte.UI.TextEditing.Compose.Definitions.TextBox;

namespace Lumyte.UI.Windowing.TextInput;

/// <summary>Connects a borrowed surface and explicit Window identity to the text input service.</summary>
public sealed class UiTextInputConnection : IUiTextInputHandler, IDisposable, ITextInputQuiescence
{
    private static readonly ConditionalWeakTable<TextDocument, DocumentOwner> _documentOwners = new();
    private readonly UiSurface _surface;
    private readonly IDisposable _inputScope;
    private readonly IUiWindowManager _windows;
    private readonly UiWindowId _window;
    private readonly ITextInputService _service;
    private readonly IUiTextInputTargetResolver _targets;
    private readonly IClipboard? _clipboard;
    private readonly Dictionary<UiAutomationId, TextBox> _editors = [];
    private readonly List<Suspension> _suspensions = [];
    private readonly ConcurrentQueue<Action> _pending = new();
    private readonly CancellationTokenSource _stoppingToken = new();
    private UiImeCandidateController? _candidateController;
    private TextBox? _editor;
    private UiTextDocumentNavigator? _navigator;
    private TextDocument? _ownedDocument;
    private int _clipboardWrites;
    private ITextInputSession? _session;
    private UiAutomationId _attachment;
    private ulong _lastKeySequence;
    private ulong _geometryRevision;
    private (Guid Session, UiTextLayout Layout, ulong Document, Vector2 Origin, WindowRect Clip, Vector2 Scale)? _publishedGeometry;
    private int _suspended;
    private bool _stopping;
    private bool _disposed;
    private Task<string?>? _paste;
    private TextBox? _pasteEditor;
    private Guid _pasteSession;
    private ulong _pasteRevision;

    /// <summary>Initializes a new instance of the <see cref = "UiTextInputConnection"/> class.</summary>
    /// <param name = "surface">The surface.</param>
    /// <param name = "windows">The windows.</param>
    /// <param name = "window">The window.</param>
    /// <param name = "service">The service.</param>
    /// <param name = "targets">The targets.</param>
    /// <param name = "clipboard">The clipboard.</param>
    public UiTextInputConnection(UiSurface surface, IUiWindowManager windows, UiWindowId window, ITextInputService service, IUiTextInputTargetResolver targets, IClipboard? clipboard = null)
        : this(surface, windows, window, service, targets, clipboard, false)
    {
    }

    internal UiTextInputConnection(UiSurface surface, IUiWindowManager windows, UiWindowId window, ITextInputService service, IUiTextInputTargetResolver targets, IClipboard? clipboard, bool staged)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(targets);
        if (!staged && !windows.IsBound(window, surface))
        {
            throw new ArgumentException("The surface must be bound to the supplied window.", nameof(window));
        }

        _surface = surface;
        _windows = windows;
        _window = window;
        _service = service;
        _targets = targets;
        _clipboard = clipboard;
        _inputScope = UiTextInputScope.Attach(surface, this);
    }

    /// <summary>Gets a value indicating whether this option is enabled.</summary>
    public bool IsStopped => (_candidateController?.IsStopped ?? true) && _stopping && _session is null && _paste is null && _suspended == 0 && _clipboardWrites == 0;

    /// <summary>Gets a value indicating whether this option is enabled.</summary>
    public bool IsInputIdle => !_stopping && _session?.Status.IsComposing != true && (_session is null || _session.Status.IsActive) && _paste is null && (_service is not ITextInputQuiescence quiet || quiet.IsInputIdle);

    /// <summary>Gets the candidate session.</summary>
    public ITextInputCandidateSession? CandidateSession => _session as ITextInputCandidateSession;

    /// <summary>Gets the latest published geometry.</summary>
    public TextInputGeometry? Geometry { get; private set; }

    /// <summary>Gets a value indicating whether candidate commands can acquire the scope without interrupting composition.</summary>
    public bool IsCandidateInputIdle => !_stopping && _paste is null && _service is ITextInputInputGate { IsCandidateInputIdle: true };

    /// <summary>Gets the owning window.</summary>
    internal UiWindowId Window => _window;

    internal UiSurface Surface => _surface;

    /// <summary>Excludes physical edits for an automation operation on this scope.</summary>
    /// <param name = "allowComposition">Allows updates to the current composition.</param>
    /// <returns>The owner-thread gate.</returns>
    public IDisposable AcquireInputGate(bool allowComposition = false) => (_service as ITextInputInputGate ?? throw new NotSupportedException("The text backend does not provide exclusive editing.")).AcquireInputGate(allowComposition);

    /// <summary>Gets the attached editable control while the caller borrows the surface.</summary>
    /// <param name = "id">The current attachment identity.</param>
    /// <returns>The matching focused editor.</returns>
    public TextBox? GetFocusedEditor(UiAutomationId id) => _surface.FocusedWidget is TextBox editor && editor.AutomationId == id && ReferenceEquals(editor.InputHandler, this) ? editor : null;

    /// <summary>Handle Input.</summary>
    /// <param name = "editor">The editor.</param>
    /// <param name = "context">The context.</param>
    /// <returns>The operation result.</returns>
    public UiInputResult HandleInput(TextBox editor, in UiInputContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _editors[editor.AutomationId] = editor;
        if (_stopping)
        {
            return UiInputResult.Handled;
        }

        if (context.Input is UiPointerInput pointer)
        {
            if (pointer.Phase == UiPointerPhase.Down)
            {
                context.RequestFocus();
                context.CapturePointer();
                if (editor.Document?.CaptureSnapshot().Composition is not null)
                {
                    _session?.RequestEnd(TextCompositionEnd.Cancel);
                }
                else if (context.PointerPosition is { } position)
                {
                    editor.SetPointerSelection(position, false);
                }
            }
            else if (pointer.Phase == UiPointerPhase.Move && context.IsCaptured && context.PointerPosition is { } position && editor.Document?.CaptureSnapshot().Composition is null)
            {
                editor.SetPointerSelection(position, true);
            }

            return UiInputResult.Handled;
        }

        if (context.Input is UiKeyInput key)
        {
            if (key.Key == Key.Tab)
            {
                return UiInputResult.Unhandled;
            }

            if (!key.IsDown)
            {
                return UiInputResult.Handled;
            }

            context.CaptureKey();
            if (editor.Document is null || editor.Document.CaptureSnapshot().Composition is not null)
            {
                return UiInputResult.Handled;
            }

            bool control = (key.Modifiers & (UiModifiers.Control | UiModifiers.Meta)) != 0;
            bool extend = (key.Modifiers & UiModifiers.Shift) != 0;
            if (control && key.Key is Key.C or Key.X)
            {
                Copy(editor, key.Key == Key.X);
            }
            else if (control && key.Key == Key.V && !editor.IsReadOnly)
            {
                BeginPaste(editor);
            }
            else
            {
                TextEditCommand? command = key.Key switch
                {
                    Key.ArrowLeft => TextEditCommand.Previous,
                    Key.ArrowRight => TextEditCommand.Next,
                    Key.Home => TextEditCommand.Start,
                    Key.End => TextEditCommand.End,
                    Key.Backspace when !editor.IsReadOnly => TextEditCommand.DeleteBackward,
                    Key.Delete when !editor.IsReadOnly => TextEditCommand.DeleteForward,
                    Key.A when control => TextEditCommand.SelectAll,
                    _ => null,
                };
                if (command is { } edit)
                {
                    TextEditingCommands.Execute(editor.Document, editor, edit, extend);
                }
            }

            return UiInputResult.Handled;
        }

        return UiInputResult.Unhandled;
    }

    /// <summary>On Pointer Capture Changed.</summary>
    /// <param name = "editor">The editor.</param>
    /// <param name = "state">The state.</param>
    public void OnPointerCaptureChanged(TextBox editor, UiPointerCaptureState state) => _editors[editor.AutomationId] = editor;

    /// <summary>Reset.</summary>
    public void Reset()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _session?.RequestEnd(TextCompositionEnd.Cancel);
        Geometry = null;
    }

    /// <summary>Synchronize Focus.</summary>
    public void SynchronizeFocus()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        AdoptRebuiltEditor();
        bool liveWindow = _windows.TryGetSnapshot(_window, out UiWindowSnapshot? focusWindow) && focusWindow is { IsVisible: true, State: UiWindowState.Open }
            && (focusWindow.IsActive || (_windows.TryGetSnapshot(focusWindow.KeyboardRoot, out UiWindowSnapshot? keyboardRoot) && keyboardRoot is { IsActive: true, State: UiWindowState.Open }));
        TextBox? desired = liveWindow && !_stopping && _suspended == 0 ? _surface.FocusedWidget as TextBox : null;
        if (desired is not null && (!ReferenceEquals(desired.InputHandler, this) || desired.Document is null || desired.LayoutProvider is null || desired.IsReadOnly || !desired.IsEnabled || !desired.IsVisible))
        {
            desired = null;
        }

        if (_session is not null && (_session.IsReleased || !ReferenceEquals(desired, _editor) || !SameAttachment(_editor, _attachment) || !ReferenceEquals(_editor?.Document, _ownedDocument)))
        {
            _session.RequestEnd(TextCompositionEnd.Cancel);
        }

        if (_session is not null && desired is not null && ReferenceEquals(desired.Document, _ownedDocument) && desired.HasSourceValueChange
            && (_session.Status.IsComposing || _session is ITextInputQuiescence { IsInputIdle: false } || !_session.Status.IsActive))
        {
            desired.SourceUpdatesBlocked = true;
            _session.RequestEnd(TextCompositionEnd.Cancel);
            if (!_session.IsReleased)
            {
                return;
            }
        }

        if (_session?.IsReleased == true)
        {
            if (_editor is not null)
            {
                _editor.SourceUpdatesBlocked = false;
            }

            _session.Dispose();
            _session = null;
            _publishedGeometry = null;
            if (_ownedDocument is { } document && _documentOwners.TryGetValue(document, out DocumentOwner? owner) && ReferenceEquals(owner.Connection, this))
            {
                _documentOwners.Remove(document);
            }

            _editor = null;
            _navigator = null;
            _ownedDocument = null;
            Geometry = null;
        }

        if (_session is not null)
        {
            if (ReferenceEquals(desired, _editor))
            {
                desired?.SynchronizeText();
            }

            return;
        }

        if (desired is null || !desired.SynchronizeText())
        {
            return;
        }

        TextDocument documentToOwn = desired.Document!;
        if (_documentOwners.TryGetValue(documentToOwn, out DocumentOwner? existing) && !ReferenceEquals(existing.Connection, this))
        {
            throw new InvalidOperationException("A document already has a connected editor.");
        }

        if (!_windows.TryGetSnapshot(_window, out UiWindowSnapshot? window) || window is null || !window.IsVisible)
        {
            return;
        }

        UiAutomationId attachment = FindAttachment(desired);
        if (attachment.Value == 0)
        {
            return;
        }

        TextInputTarget target = _targets.Resolve(window.KeyboardRoot, window.PresentationRoot, attachment.SurfaceId, Guid.NewGuid());
        _navigator = new UiTextDocumentNavigator(desired.LayoutProvider!);
        ITextInputSession session = _service.Begin(target, documentToOwn, _navigator);
        _documentOwners.Remove(documentToOwn);
        _documentOwners.Add(documentToOwn, new(this));
        _editor = desired;
        _ownedDocument = documentToOwn;
        _attachment = attachment;
        _session = session;
        _lastKeySequence = 0;
        _editors[attachment] = desired;
    }

    /// <summary>Dispatch Key.</summary>
    /// <param name = "key">The key.</param>
    /// <returns>The operation result.</returns>
    public bool DispatchKey(TextInputKey key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_session is null || !_session.Status.IsActive || key.SessionId != _session.Status.SessionId || key.Generation != _session.Status.Generation || key.Sequence <= _lastKeySequence)
        {
            return false;
        }

        _lastKeySequence = key.Sequence;
        UiModifiers modifiers = (key.Shift ? UiModifiers.Shift : 0) | (key.Control ? UiModifiers.Control : 0) | (key.Alt ? UiModifiers.Alt : 0) | (key.Meta ? UiModifiers.Meta : 0);
        return _surface.Dispatch(new UiKeyInput(key.Key, key.IsDown, key.IsRepeat, modifiers));
    }

    /// <summary>Advance.</summary>
    public void Advance()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int pendingCount = _pending.Count;
        for (int index = 0; index < pendingCount && _pending.TryDequeue(out Action? action); index++)
        {
            action();
        }

        SynchronizeFocus();
        if (_session is null && (_candidateController?.IsPresentationIdle ?? true))
        {
            foreach (Suspension suspension in _suspensions.ToArray())
            {
                _suspensions.Remove(suspension);
                suspension.Registration.Dispose();
                if (suspension.IsCanceled)
                {
                    _suspended--;
                    suspension.Completion.TrySetCanceled(suspension.Cancellation);
                }
                else
                {
                    suspension.Completion.TrySetResult(new SuspensionToken(() => _pending.Enqueue(() => _suspended--)));
                }
            }
        }

        if (_paste?.IsCompleted == true)
        {
            Task<string?> pending = _paste;
            _paste = null;
            TextBox? pasteEditor = _pasteEditor;
            _pasteEditor = null;
            string? text;
            try
            {
                text = pending.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) when (_stopping)
            {
                text = null;
            }

            if (!_stopping && text is not null && _session?.Status.SessionId == _pasteSession && pasteEditor?.Document?.CaptureSnapshot().Revision == _pasteRevision && ReferenceEquals(_editor, pasteEditor) && !pasteEditor.IsReadOnly)
            {
                TextEditingCommands.ReplaceSelection(pasteEditor.Document!, text, TextEditOrigin.Clipboard);
            }

            _pasteEditor = null;
        }

        var failures = new List<Exception>();
        var documentChanges = new Dictionary<TextDocument, IReadOnlyList<TextDocumentChange>>();
        foreach ((UiAutomationId identity, TextBox previous) in _editors.ToArray())
        {
            TextBox? editor = ReferenceEquals(previous.InputHandler, this) ? previous : _surface.FindWidget(identity) as TextBox;
            if (editor is null || !ReferenceEquals(editor.InputHandler, this))
            {
                _editors.Remove(identity);
                continue;
            }

            _editors[identity] = editor;
            if (editor.Document is not { } document)
            {
                continue;
            }

            if (!documentChanges.TryGetValue(document, out IReadOnlyList<TextDocumentChange>? changes))
            {
                changes = document.DrainChanges();
                documentChanges.Add(document, changes);
            }

            if (changes.Count == 0)
            {
                continue;
            }

            editor.DocumentChanged();
            TextDocumentSnapshot snapshot = document.CaptureSnapshot();
            try
            {
                TextDocumentChange? latest = changes.LastOrDefault(change => change.CommittedTextChanged && change.Origin != TextEditOrigin.Application && change.Revision > editor.SourceRevision);
                if (latest is not null)
                {
                    editor.OnTextChanged?.Invoke(new(snapshot.CommittedText, snapshot.Revision, latest.Origin));
                }
            }
            catch (Exception error)
            {
                failures.Add(error);
            }

            try
            {
                if (changes.Any(c => c.CompositionChanged))
                {
                    editor.OnCompositionChanged?.Invoke(snapshot.Composition);
                }
            }
            catch (Exception error)
            {
                failures.Add(error);
            }
        }

        if (failures.Count != 0)
        {
            throw new AggregateException(failures);
        }
    }

    /// <summary>Publish Geometry.</summary>
    public void PublishGeometry()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SynchronizeFocus();
        if (_session?.Status.IsActive != true || _editor?.Document is not { } document || _editor.CurrentLayout is not { } layout || layout.DocumentRevision != document.CaptureSnapshot().Revision || !_windows.TryGetSnapshot(_window, out UiWindowSnapshot? window) || window is null)
        {
            return;
        }

        Vector2 origin = new(window.BoundsInPresentation.X - _editor.HorizontalOffset, window.BoundsInPresentation.Y);
        for (Lumyte.UI.Compose.Definitions.Widget? node = _editor; node is not null; node = node.Parent)
        {
            origin += new Vector2(node.Bounds.X, node.Bounds.Y);
        }

        TextDocumentSnapshot snapshot = document.CaptureSnapshot();
        WindowRect clip = new(window.ClipInPresentation.X, window.ClipInPresentation.Y, window.ClipInPresentation.Width, window.ClipInPresentation.Height);
        Vector2 nodeOrigin = origin + new Vector2(_editor.HorizontalOffset, 0);
        for (Lumyte.UI.Compose.Definitions.Widget? node = _editor; node is not null; node = node.Parent)
        {
            if (node.ClipToBounds)
            {
                float left = Math.Max(clip.X, nodeOrigin.X);
                float top = Math.Max(clip.Y, nodeOrigin.Y);
                float right = Math.Min(clip.X + clip.Width, nodeOrigin.X + node.Bounds.Width);
                float bottom = Math.Min(clip.Y + clip.Height, nodeOrigin.Y + node.Bounds.Height);
                clip = new(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
            }

            nodeOrigin -= new Vector2(node.Bounds.X, node.Bounds.Y);
        }

        (Guid Session, UiTextLayout Layout, ulong Document, Vector2 Origin, WindowRect Clip, Vector2 Scale) key = (_session.Status.SessionId, layout, snapshot.Revision, origin, clip, window.Scale);
        if (_publishedGeometry == key)
        {
            return;
        }

        Rect2D caret = layout.GetCaretBounds(new(snapshot.Selection.Active, snapshot.Selection.ActiveAffinity));
        WindowRect Convert(Rect2D rect) => new(rect.X + origin.X, rect.Y + origin.Y, rect.Width, rect.Height);
        TextInputCaretGeometry[] carets = layout.CaretStops.Select(c => new TextInputCaretGeometry(c.Position, c.Affinity, Convert(layout.GetCaretBounds(c)))).ToArray();
        int[] boundaries = TextDocument.GraphemeBoundaries(snapshot.Text);
        var ranges = new List<TextInputRangeGeometry>();
        for (int i = 0; i < boundaries.Length - 1; i++)
        {
            var span = new TextSpan(boundaries[i], boundaries[i + 1] - boundaries[i]);
            ranges.AddRange(layout.GetRangeBounds(span).Select(rect => new TextInputRangeGeometry(span, Convert(rect))));
        }

        Geometry = new(snapshot.Revision, checked(++_geometryRevision), clip, Convert(caret), Array.AsReadOnly(ranges.ToArray()), Array.AsReadOnly(carets));
        _session.UpdateGeometry(Geometry);
        _publishedGeometry = key;
    }

    /// <summary>Suspend Async.</summary>
    /// <param name = "cancellationToken">The cancellation token.</param>
    /// <returns>The operation result.</returns>
    public ValueTask<IAsyncDisposable> SuspendAsync(CancellationToken cancellationToken = default) => SuspendAsync(TextCompositionEnd.Cancel, cancellationToken);

    /// <summary>Suspend Async.</summary>
    /// <param name = "behavior">The behavior.</param>
    /// <param name = "cancellationToken">The cancellation token.</param>
    /// <returns>The operation result.</returns>
    public ValueTask<IAsyncDisposable> SuspendAsync(TextCompositionEnd behavior, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        _suspended++;
        var suspension = new Suspension(cancellationToken);
        suspension.Registration = cancellationToken.Register(() => suspension.IsCanceled = true);
        _suspensions.Add(suspension);
        _session?.RequestEnd(behavior);
        return new(suspension.Completion.Task);
    }

    /// <summary>Begin Stop.</summary>
    public void BeginStop()
    {
        if (_disposed)
        {
            return;
        }

        _stopping = true;
        _candidateController?.BeginStop();
        _stoppingToken.Cancel();
        _session?.RequestEnd(TextCompositionEnd.Cancel);
    }

    /// <summary>Dispose.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (!IsStopped)
        {
            throw new InvalidOperationException("BeginStop and Advance must drain the connection before disposal.");
        }

        _inputScope.Dispose();
        _editors.Clear();
        _stoppingToken.Dispose();
        _disposed = true;
    }

    internal void AttachCandidateController(UiImeCandidateController controller)
    {
        if (_candidateController is not null)
        {
            throw new InvalidOperationException("A connection already has a candidate controller.");
        }

        _candidateController = controller;
    }

    internal void DetachCandidateController(UiImeCandidateController controller)
    {
        if (ReferenceEquals(_candidateController, controller))
        {
            _candidateController = null;
        }
    }

    private void AdoptRebuiltEditor()
    {
        if (_editor is null || ReferenceEquals(_editor.InputHandler, this))
        {
            return;
        }

        if (_surface.FindWidget(_attachment) is TextBox replacement && ReferenceEquals(replacement.InputHandler, this) && ReferenceEquals(replacement.Document, _ownedDocument))
        {
            if (ReferenceEquals(_pasteEditor, _editor))
            {
                _pasteEditor = replacement;
            }

            _editor = replacement;
            _editors[_attachment] = replacement;
            if (replacement.LayoutProvider is { } provider)
            {
                _navigator?.UpdateProvider(provider);
            }
        }
    }

    private UiAutomationId FindAttachment(TextBox editor) => editor.AutomationId.SurfaceId == _surface.Root.AutomationId.SurfaceId ? editor.AutomationId : default;

    private bool SameAttachment(TextBox? editor, UiAutomationId attachment) => editor is not null && editor.AutomationId == attachment && attachment.Value != 0;

    private void Copy(TextBox editor, bool cut)
    {
        if (_clipboard is null || editor.Document is null)
        {
            return;
        }

        TextDocumentSnapshot snapshot = editor.Document.CaptureSnapshot();
        int start = Math.Min(snapshot.Selection.Anchor, snapshot.Selection.Active);
        int length = Math.Abs(snapshot.Selection.Active - snapshot.Selection.Anchor);
        ValueTask write = _clipboard.SetTextAsync(snapshot.CommittedText.Substring(start, length), _stoppingToken.Token);
        if (!write.IsCompletedSuccessfully)
        {
            _clipboardWrites++;
            Guid session = _session?.Status.SessionId ?? Guid.Empty;
            Task task = write.AsTask();
            _pending.Enqueue(() => ObserveClipboard(task, editor, session, snapshot.Revision, cut, start, length));
        }
        else if (cut && !editor.IsReadOnly)
        {
            editor.Document.TryReplace(snapshot.Revision, new(start, length), string.Empty, new(start, start), TextEditOrigin.Clipboard);
        }
    }

    private void ObserveClipboard(Task write, TextBox editor, Guid session, ulong revision, bool cut, int start, int length)
    {
        if (!write.IsCompleted)
        {
            _pending.Enqueue(() => ObserveClipboard(write, editor, session, revision, cut, start, length));
            return;
        }

        _clipboardWrites--;
        try
        {
            write.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (_stopping)
        {
            return;
        }

        if (_session?.Status.SessionId == session && !_stopping && cut && !editor.IsReadOnly && ReferenceEquals(_editor, editor) && editor.Document?.CaptureSnapshot().Revision == revision)
        {
            editor.Document.TryReplace(revision, new(start, length), string.Empty, new(start, start), TextEditOrigin.Clipboard);
        }
    }

    private void BeginPaste(TextBox editor)
    {
        if (_clipboard is null || _paste is not null || editor.Document is null || _session is null)
        {
            return;
        }

        _pasteEditor = editor;
        _pasteRevision = editor.Document.CaptureSnapshot().Revision;
        _pasteSession = _session.Status.SessionId;
        _paste = _clipboard.GetTextAsync(_stoppingToken.Token).AsTask();
    }

    private sealed record DocumentOwner(UiTextInputConnection Connection);

    private sealed class Suspension(CancellationToken cancellation)
    {
        private int _isCanceled;

        /// <summary>Gets or sets a value indicating whether the caller canceled its wait.</summary>
        public bool IsCanceled { get => Volatile.Read(ref _isCanceled) != 0; set => Volatile.Write(ref _isCanceled, value ? 1 : 0); }

        /// <summary>Gets the completion.</summary>
        public TaskCompletionSource<IAsyncDisposable> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Gets the cancellation.</summary>
        public CancellationToken Cancellation { get; } = cancellation;

        /// <summary>Gets or sets the registration.</summary>
        public CancellationTokenRegistration Registration { get; set; }
    }

    private sealed class SuspensionToken(Action release) : IAsyncDisposable
    {
        private Action? _release = release;

        /// <summary>Dispose Async.</summary>
        /// <returns>The operation result.</returns>
        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _release, null)?.Invoke();
            return ValueTask.CompletedTask;
        }
    }
}
