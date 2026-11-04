using System.Runtime.InteropServices;
using Lumyte.Platform.Windows.TextInput.Interop;
using Lumyte.TextEditing;

namespace Lumyte.Platform.Windows.TextInput;

internal sealed unsafe partial class WindowsCandidateSource : ITfUIElementSink.Interface
{
    private readonly Func<WindowsTextSession?> _current;
    private readonly Queue<PendingCommand> _commands = new();
    private readonly List<Confirmation> _confirmations = [];
    private ComPtr<ITfUIElementMgr>? _manager;
    private ComPtr<ITfSource>? _source;
    private uint _cookie;
    private bool _advised;
    private ImeCandidateUiPolicy _policy;
    private bool _prepared;
    private ComPtr<ITfCandidateListUIElement>? _element;
    private ComPtr<ITfCandidateListUIElementBehavior>? _behavior;
    private WindowsTextSession? _owner;
    private uint _nativeId;
    private ulong _generation;
    private ulong _revision;
    private ulong _serial;
    private uint _dirtyFlags;
    private bool _dirty;
    private bool _ended;
    private bool _validated;
    private TaskCompletionSource? _returning;
    private ImeCandidateSnapshot? _snapshot;

    private ImeCandidateOperations _operations;

    private ImeCandidateState _state = new(null, ImeCandidatePresentationState.System, string.Empty);

    internal WindowsCandidateSource(ComPtr<ITfThreadMgr> threadManager, Func<WindowsTextSession?> current)
    {
        _current = current;
        nint thread = TsfCom.Borrow(threadManager);
        try
        {
            try
            {
                _manager = TsfCom.Query<ITfUIElementMgr>(thread);
                _source = TsfCom.Query<ITfSource>(thread);
            }
            catch (COMException)
            {
                TsfCom.Release(_manager);
                TsfCom.Release(_source);
                _manager = null;
                _source = null;
                return;
            }

            nint sink = TsfCom.Borrow<ITfUIElementSink>(this);
            try
            {
                Guid iid = TsfCom.GuidOf<ITfUIElementSink>();
                uint cookie;
                Marshal.ThrowExceptionForHR(_source.Pointer->AdviseSink(&iid, (IUnknown*)sink, &cookie));
                _cookie = cookie;
                _advised = true;
            }
            finally
            {
                TsfCom.Return(sink);
            }
        }
        catch
        {
            if (_advised)
            {
                _source!.Pointer->UnadviseSink(_cookie);
                _advised = false;
            }

            TsfCom.Release(_source);
            TsfCom.Release(_manager);
            _source = null;
            _manager = null;
            throw;
        }
        finally
        {
            TsfCom.Return(thread);
        }
    }

    internal ImeCandidateSupport Support => new(_manager is not null, _manager is not null, _manager is not null, _manager is null ? "system-ui-only" : string.Empty);

    internal ImeCandidateState State => _state;

    internal bool IsIdle => !_dirty && _commands.Count == 0 && _confirmations.Count == 0 && (_returning is null || _returning.Task.IsCompleted);

    /// <summary>Begin UIElement.</summary>
    /// <param name = "elementId">The element id.</param>
    /// <param name = "show">The show.</param>
    /// <returns>The operation result.</returns>
    public int BeginUIElement(uint elementId, int* show)
    {
        if (show == null)
        {
            return unchecked((int)0x80004003);
        }

        *show = 1;
        try
        {
            WindowsTextSession? session = _current();
            if (_manager is null || _element is not null || !_prepared || _policy != ImeCandidateUiPolicy.PreferCustom || session?.Status.IsActive != true)
            {
                return 0;
            }

            nint element = 0;
            Marshal.ThrowExceptionForHR(_manager.Pointer->GetUIElement(elementId, (ITfUIElement**)&element));
            try
            {
                _element = TsfCom.Query<ITfCandidateListUIElement>(element);
                try
                {
                    _behavior = TsfCom.Query<ITfCandidateListUIElementBehavior>(element);
                }
                catch (COMException)
                {
                    _behavior = null;
                }
            }
            finally
            {
                TsfCom.Return(element);
            }

            _generation = checked(_generation + 1);
            _serial = _serial == ulong.MaxValue ? ulong.MaxValue : _serial + 1;
            _revision = 0;
            _nativeId = elementId;
            _owner = session;
            _ended = false;
            _validated = false;
            _dirty = false;
            _dirtyFlags = 0;
            _snapshot = null;
            _operations = _behavior is null ? ImeCandidateOperations.None : ImeCandidateOperations.Select | ImeCandidateOperations.Page | ImeCandidateOperations.Finalize | ImeCandidateOperations.Abort;
            var id = new ImeCandidateListId(session.Status.SessionId, session.Generation, _generation);
            _state = new(id, ImeCandidatePresentationState.Preparing, string.Empty);
            *show = 0;
        }
        catch (Exception)
        {
            ClearElement();
            _state = new(null, ImeCandidatePresentationState.System, "candidate-unavailable");
            *show = 1;
        }

        return 0;
    }

    /// <summary>Update UIElement.</summary>
    /// <param name = "elementId">The element id.</param>
    /// <returns>The operation result.</returns>
    public int UpdateUIElement(uint elementId)
    {
        try
        {
            if (_element is not null && !_ended && elementId == _nativeId)
            {
                uint flags = 0;
                Marshal.ThrowExceptionForHR(_element.Pointer->GetUpdatedFlags(&flags));
                _dirtyFlags |= flags;
                _dirty = true;
                _serial = _serial == ulong.MaxValue ? ulong.MaxValue : _serial + 1;
            }
        }
        catch (Exception)
        {
            _state = _state with
            {
                Code = "candidate-read-failed",
            };
            ReturnToSystem();
        }

        return 0;
    }

    /// <summary>End UIElement.</summary>
    /// <param name = "elementId">The element id.</param>
    /// <returns>The operation result.</returns>
    public int EndUIElement(uint elementId)
    {
        if (_element is not null && elementId == _nativeId)
        {
            _ended = true;
            _dirty = false;
            _snapshot = null;
            _serial = _serial == ulong.MaxValue ? ulong.MaxValue : _serial + 1;
            _state = new(null, ImeCandidatePresentationState.Closed, string.Empty);
        }

        return 0;
    }

    internal void Configure(WindowsTextSession session, ImeCandidateUiPolicy policy, bool prepared)
    {
        if (!ReferenceEquals(_current(), session))
        {
            return;
        }

        _policy = policy;
        _prepared = prepared;
        if ((!prepared || policy == ImeCandidateUiPolicy.System) && _element is not null)
        {
            ReturnToSystem();
        }
    }

    internal ImeCandidateSnapshot? Capture(WindowsTextSession session) => ReferenceEquals(session, _owner) && _validated && _state.State is ImeCandidatePresentationState.Custom or ImeCandidatePresentationState.ReturningToSystem or ImeCandidatePresentationState.Failed ? _snapshot : null;

    internal ValueTask<ImeCandidateResult> Request(WindowsTextSession session, ImeCandidateAction action, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (_commands.Count >= 4096)
        {
            throw new InvalidOperationException("Candidate command queue is full.");
        }

        var completion = new TaskCompletionSource<ImeCandidateResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _commands.Enqueue(new(session, action, cancellation, completion));
        return new(completion.Task);
    }

    internal ValueTask ReturnToSystem()
    {
        if (_element is null || _ended)
        {
            return ValueTask.CompletedTask;
        }

        _returning ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
        _state = new(_snapshot?.Id, ImeCandidatePresentationState.ReturningToSystem, _state.Code);
        return new(_returning.Task);
    }

    internal void Advance()
    {
        if (_ended)
        {
            ClearElement();
            _returning?.TrySetResult();
            _returning = null;
        }

        if (_returning is { } returning && !returning.Task.IsCompleted && _element is { } element)
        {
            try
            {
                Marshal.ThrowExceptionForHR(element.Pointer->Show(new BOOL(1)));
                int shown = 0;
                Marshal.ThrowExceptionForHR(element.Pointer->IsShown((BOOL*)&shown));
                if (shown == 0 && !_ended)
                {
                    throw new COMException("The TIP did not restore the System candidate UI.");
                }

                _state = new(null, _ended ? ImeCandidatePresentationState.Closed : ImeCandidatePresentationState.System, _state.Code);
                ClearElement();
                returning.TrySetResult();
                _returning = null;
            }
            catch (Exception error)
            {
                _state = _state with
                {
                    State = ImeCandidatePresentationState.Failed,

                    Code = "candidate-system-restore-failed",
                };
                _prepared = false;

                returning.TrySetException(error);

                // Keep native references until End/deactivation; do not infer a document owner.
            }
        }

        if (_snapshot is { } observed && _current() is { } session && observed.DocumentRevision != session.Document.CaptureSnapshot().Revision)
        {
            _dirty = true;
        }

        if (_dirty && _element is not null && _returning is null)
        {
            ReadCandidates();
        }

        foreach (Confirmation confirmation in _confirmations.ToArray())
        {
            PendingCommand pending = confirmation.Pending;
            ImeCandidateSnapshot? current = _snapshot;
            bool matched = current?.Id == pending.Action.Id && (pending.Action.Kind == ImeCandidateActionKind.Page ? current.CurrentPage == pending.Action.Index : current.SelectedIndex == pending.Action.Index);
            bool unavailable = current is null || current.Id != pending.Action.Id || _state.State != ImeCandidatePresentationState.Custom;
            bool expired = TimeProvider.System.GetElapsedTime(confirmation.Started) >= TimeSpan.FromSeconds(2);
            if (pending.Cancellation.IsCancellationRequested || matched || unavailable || expired)
            {
                _confirmations.Remove(confirmation);
                if (pending.Cancellation.IsCancellationRequested)
                {
                    pending.Completion.TrySetCanceled(pending.Cancellation);
                }
                else
                {
                    pending.Completion.TrySetResult(new(pending.Action, matched, matched ? "accepted" : unavailable ? "candidate-unavailable" : "candidate-update-timeout"));
                }
            }
        }

        int commands = _commands.Count;
        for (int i = 0; i < commands; i++)
        {
            PendingCommand pending = _commands.Dequeue();
            if (pending.Cancellation.IsCancellationRequested)
            {
                pending.Completion.TrySetCanceled(pending.Cancellation);
                continue;
            }

            try
            {
                ImeCandidateResult result = Execute(pending);
                if (result.Accepted && pending.Action.Kind is ImeCandidateActionKind.Select or ImeCandidateActionKind.Page)
                {
                    _confirmations.Add(new(pending, TimeProvider.System.GetTimestamp()));
                    _dirty = true;
                }
                else
                {
                    pending.Completion.TrySetResult(result);
                }
            }
            catch (Exception)
            {
                pending.Completion.TrySetResult(new(pending.Action, false, "candidate-command-failed"));
            }
        }
    }

    internal void Unadvise()
    {
        if (_advised)
        {
            Marshal.ThrowExceptionForHR(_source!.Pointer->UnadviseSink(_cookie));
            _advised = false;
        }
    }

    internal void ReleaseAfterDeactivation()
    {
        ClearElement();
        foreach (Confirmation confirmation in _confirmations)
        {
            confirmation.Pending.Completion.TrySetResult(new(confirmation.Pending.Action, false, "candidate-unavailable"));
        }

        _confirmations.Clear();
        while (_commands.TryDequeue(out PendingCommand? pending))
        {
            pending.Completion.TrySetResult(new(pending.Action, false, "candidate-unavailable"));
        }

        _returning?.TrySetResult();
        _returning = null;
        TsfCom.Release(_source);
        TsfCom.Release(_manager);
        _source = null;
        _manager = null;
    }

    private void ReadCandidates()
    {
        ulong serial = _serial;
        try
        {
            if (_owner is null || !ReferenceEquals(_owner, _current()) || !_owner.Status.IsActive || !MatchesDocument())
            {
                _state = _state with
                {
                    Code = "candidate-document-unavailable",
                };
                ReturnToSystem();
                return;
            }

            _validated = true;
            ComPtr<ITfCandidateListUIElement> element = _element!;
            uint count = 0;
            Marshal.ThrowExceptionForHR(element.Pointer->GetCount(&count));
            if (count > 1024)
            {
                throw new InvalidDataException("Candidate count exceeds the limit.");
            }

            var items = new List<ImeCandidateItem>();
            int total = 0;
            for (uint index = 0; index < count; index++)
            {
                nint value = 0;
                Marshal.ThrowExceptionForHR(element.Pointer->GetString(index, (BSTR*)&value));
                try
                {
                    int bytes = value == 0 ? 0 : Marshal.ReadInt32(value, -4);
                    if (bytes < 0 || bytes > 8192 || (bytes & 1) != 0 || checked(total + (bytes / 2)) > 262144)
                    {
                        throw new InvalidDataException("Candidate text exceeds the limit.");
                    }

                    string text = value == 0 ? string.Empty : Marshal.PtrToStringUni(value, bytes / 2)!;
                    total += text.Length;
                    items.Add(new((int)index, text));
                }
                finally
                {
                    if (value != 0)
                    {
                        Marshal.FreeBSTR(value);
                    }
                }
            }

            uint selection = 0;
            int selectedResult = element.Pointer->GetSelection(&selection);
            Marshal.ThrowExceptionForHR(selectedResult);
            int? selected = count == 0 || selectedResult == 1 ? null : checked((int)selection);
            uint pageCount = 0;
            Marshal.ThrowExceptionForHR(element.Pointer->GetPageIndex(null, 0, &pageCount));
            if (pageCount > 1024)
            {
                throw new InvalidDataException("Candidate page limit exceeded.");
            }

            uint[] pages = new uint[pageCount];
            fixed (uint* pageData = pages)
            {
                uint actual = pageCount;
                Marshal.ThrowExceptionForHR(element.Pointer->GetPageIndex(pageData, pageCount, &actual));
                if (actual != pageCount)
                {
                    _dirty = true;
                    return;
                }
            }

            uint currentPage = 0;
            if (count != 0 && pageCount != 0)
            {
                Marshal.ThrowExceptionForHR(element.Pointer->GetCurrentPage(&currentPage));
            }

            if (_ended || serial != _serial || !MatchesDocument())
            {
                _dirty = !_ended;
                return;
            }

            var id = new ImeCandidateListId(_owner.Status.SessionId, _owner.Generation, _generation);
            var snapshot = new ImeCandidateSnapshot(id, checked(_revision + 1), _owner.Document.CaptureSnapshot().Revision, items, selected, pages.Select(p => checked((int)p)).ToArray(), count == 0 || pageCount == 0 ? null : checked((int)currentPage), _operations);
            _snapshot = ImeCandidateValidation.Copy(snapshot);
            _revision = snapshot.Revision;
            _dirty = false;
            _dirtyFlags = 0;
            _state = new(id, ImeCandidatePresentationState.Custom, string.Empty);
        }
        catch (Exception)
        {
            _state = _state with
            {
                Code = "invalid-candidates",
            };
            ReturnToSystem();
        }
    }

    private ImeCandidateResult Execute(PendingCommand pending)
    {
        ImeCandidateAction action = pending.Action;
        ImeCandidateSnapshot? snapshot = _snapshot;
        if (_ended || _element is null || snapshot is null || !_validated || !ReferenceEquals(pending.Session, _owner) || _state.State != ImeCandidatePresentationState.Custom)
        {
            return new(action, false, "candidate-unavailable");
        }

        if (action.Id != snapshot.Id || action.ExpectedRevision != snapshot.Revision || !MatchesDocument())
        {
            return new(action, false, "stale-candidate");
        }

        ImeCandidateOperations operation = action.Kind switch
        {
            ImeCandidateActionKind.Select => ImeCandidateOperations.Select,
            ImeCandidateActionKind.Page => ImeCandidateOperations.Page,
            ImeCandidateActionKind.Finalize => ImeCandidateOperations.Finalize,
            ImeCandidateActionKind.Abort => ImeCandidateOperations.Abort,
            _ => ImeCandidateOperations.None,
        };
        if (_behavior is null || operation == ImeCandidateOperations.None || (_operations & operation) == 0)
        {
            return new(action, false, "unsupported-operation");
        }

        int index = action.Index ?? -1;
        if ((action.Kind == ImeCandidateActionKind.Select && (index < 0 || index >= snapshot.Items.Count)) || (action.Kind == ImeCandidateActionKind.Page && (index < 0 || index >= snapshot.PageStarts.Count)) || (action.Kind is ImeCandidateActionKind.Finalize or ImeCandidateActionKind.Abort && action.Index is not null) || (action.Kind == ImeCandidateActionKind.Finalize && snapshot.SelectedIndex is null))
        {
            return new(action, false, "invalid-candidate-index");
        }

        int result;
        try
        {
            result = action.Kind switch
            {
                ImeCandidateActionKind.Select => _behavior.Pointer->SetSelection((uint)index),
                ImeCandidateActionKind.Page => _behavior.Pointer->SetSelection((uint)snapshot.PageStarts[index]),
                ImeCandidateActionKind.Finalize => _behavior.Pointer->Finalize(),
                _ => _behavior.Pointer->Abort(),
            };
        }
        catch (COMException error)
        {
            result = error.HResult;
        }

        if (result == unchecked((int)0x80004001))
        {
            _operations &= ~operation;
            if (_snapshot is not null && !_ended)
            {
                _snapshot = _snapshot with
                {
                    Operations = _operations,
                    Revision = checked(++_revision),
                };
            }

            return new(action, false, "unsupported-operation");
        }

        return new(action, result >= 0, result >= 0 ? "accepted" : "candidate-command-failed");
    }

    private bool MatchesDocument()
    {
        if (_element is null || _owner?.DocumentPointer is not { } expected || expected == 0)
        {
            return false;
        }

        nint document = 0;
        try
        {
            Marshal.ThrowExceptionForHR(_element.Pointer->GetDocumentMgr((ITfDocumentMgr**)&document));
            return document != 0 && TsfCom.SameIdentity(document, expected);
        }
        finally
        {
            TsfCom.Return(document);
        }
    }

    private void ClearElement()
    {
        TsfCom.Release(_behavior);
        TsfCom.Release(_element);
        _behavior = null;
        _element = null;
        _owner = null;
        _snapshot = null;
        _dirty = false;
        _validated = false;
        _ended = false;
        _dirtyFlags = 0;
    }

    private sealed record Confirmation(PendingCommand Pending, long Started);

    private sealed record PendingCommand(WindowsTextSession Session, ImeCandidateAction Action, CancellationToken Cancellation, TaskCompletionSource<ImeCandidateResult> Completion);
}
