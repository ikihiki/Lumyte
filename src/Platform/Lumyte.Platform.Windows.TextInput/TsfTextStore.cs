using System.Runtime.InteropServices;
using Lumyte.Platform.TextInput.Abstractions;
using Lumyte.Platform.Windows.TextInput.Interop;
using Lumyte.TextEditing;
using Lumyte.Windowing.Abstractions;

namespace Lumyte.Platform.Windows.TextInput;

internal sealed unsafe partial class TsfTextStore : ITextStoreACP.Interface, ITfContextOwnerCompositionSink.Interface, ITfTextEditSink.Interface
{
    internal const int InvalidPosition = unchecked((int)0x80040200);
    internal const int NoLock = unchecked((int)0x80040201);
    internal const int NoLayout = unchecked((int)0x80040206);
    internal const int SynchronousLockDenied = unchecked((int)0x80040208);
    internal const int AsyncLock = 0x40300;
    internal const int ReadOnly = unchecked((int)0x80040209);
    private const int InvalidArgument = unchecked((int)0x80070057);
    private const int PointerError = unchecked((int)0x80004003);
    private const int Unsupported = unchecked((int)0x80004001);
    private readonly TextDocument _document;
    private readonly Func<WindowRect, TsfRect> _toScreen;
    private readonly Func<nint> _getWindow;
    private readonly Func<bool> _isVisible;
    private readonly Func<bool> _isReadOnly;
    private ComPtr<ITextStoreACPSink>? _sink;
    private nint _sinkIdentity;
    private uint _sinkMask;
    private uint _lock;
    private uint _pendingLock;
    private bool _notifying;
    private TextDocumentTransaction? _edit;
    private TextDocumentSnapshot _lastObserved;
    private TextDocumentSnapshot? _cancellationBefore;
    private TextInputGeometry? _geometry;
    private Guid _compositionId;
    private nint _composition;

    private bool _layoutChanged;

    internal TsfTextStore(TextDocument document, Func<WindowRect, TsfRect> toScreen, Func<nint> getWindow, ITextStoreACPSink.Interface? sink = null, Func<bool>? isVisible = null, Func<bool>? isReadOnly = null)
    {
        _document = document;
        _toScreen = toScreen;
        _getWindow = getWindow;
        _isVisible = isVisible ?? (() => true);
        _isReadOnly = isReadOnly ?? (() => false);
        _lastObserved = document.CaptureSnapshot();
        _sink = sink is null ? null : TsfCom.Own<ITextStoreACPSink>(TsfCom.Borrow<ITextStoreACPSink>(sink));
        _sinkMask = uint.MaxValue;
    }

    internal TextCompositionEnd? RequestedEnd { get; set; }

    internal bool HasPendingWork => _lock != 0 || _pendingLock != 0 || _cancellationBefore is not null;

    internal bool HasComposition => _composition != 0;

    internal nint CompositionPointer => _composition;

    private bool Readable => (_lock & 2) != 0;

    private bool Writable => (_lock & 4) != 0 && _edit is not null;

    /// <summary>Advise Sink.</summary>
    /// <param name = "riid">The riid.</param>
    /// <param name = "punk">The punk.</param>
    /// <param name = "mask">The mask.</param>
    /// <returns>The operation result.</returns>
    public int AdviseSink(Guid* riid, nint punk, uint mask) => Boundary(() =>
    {
        if (riid == null || punk == 0)
        {
            return PointerError;
        }

        if (*riid != TsfCom.GuidOf<ITextStoreACPSink>())
        {
            return unchecked((int)0x80040202);
        }

        if (_sink is not null)
        {
            return unchecked((int)0x80040201);
        }

        _sink = TsfCom.Query<ITextStoreACPSink>(punk);
        _sinkIdentity = Identity(punk);
        _sinkMask = mask;
        return 0;
    });

    /// <summary>Unadvise Sink.</summary>
    /// <param name = "punk">The punk.</param>
    /// <returns>The operation result.</returns>
    public int UnadviseSink(nint punk) => Boundary(() =>
    {
        if (_sink is null || punk == 0 || Identity(punk) != _sinkIdentity)
        {
            return unchecked((int)0x80040200);
        }

        TsfCom.Release(_sink);
        _sink = null;
        _sinkIdentity = 0;
        return 0;
    });

    /// <summary>Request Lock.</summary>
    /// <param name = "flags">The flags.</param>
    /// <param name = "session">The session.</param>
    /// <returns>The operation result.</returns>
    public int RequestLock(uint flags, int* session) => Boundary(() =>
    {
        if (session == null)
        {
            return PointerError;
        }

        if (_sink is null || (flags & 2) == 0 || (flags & ~7U) != 0)
        {
            *session = InvalidArgument;
            return InvalidArgument;
        }

        if (_lock != 0 || _notifying)
        {
            if ((flags & 1) != 0)
            {
                *session = SynchronousLockDenied;
            }
            else
            {
                _pendingLock |= flags & 6;
                *session = AsyncLock;
            }

            return 0;
        }

        *session = GrantLock(flags);
        return 0;
    });

    /// <summary>Get Status.</summary>
    /// <param name = "status">The status.</param>
    /// <returns>The operation result.</returns>
    public int GetStatus(TsStatus* status) => Boundary(() =>
    {
        if (status == null)
        {
            return PointerError;
        }

        *status = new()
        {
            dwDynamicFlags = _isReadOnly() ? 1u : 0,
            dwStaticFlags = 8,
        };
        return 0;
    });

    /// <summary>Query Insert.</summary>
    /// <param name = "start">The start.</param>
    /// <param name = "end">The end.</param>
    /// <param name = "count">The count.</param>
    /// <param name = "resultStart">The result start.</param>
    /// <param name = "resultEnd">The result end.</param>
    /// <returns>The operation result.</returns>
    public int QueryInsert(int start, int end, uint count, int* resultStart, int* resultEnd) => Boundary(() =>
    {
        if (resultStart == null || resultEnd == null)
        {
            return PointerError;
        }

        TextDocumentSnapshot current = Current();
        if (!ValidRange(current.Text, start, end) || count > _document.MaximumLength || current.Text.Length - (end - start) + count > _document.MaximumLength)
        {
            return InvalidPosition;
        }

        *resultStart = start;
        *resultEnd = end;
        return 0;
    });

    /// <summary>Get Selection.</summary>
    /// <param name = "index">The index.</param>
    /// <param name = "count">The count.</param>
    /// <param name = "selection">The selection.</param>
    /// <param name = "fetched">The fetched.</param>
    /// <returns>The operation result.</returns>
    public int GetSelection(uint index, uint count, TsSelection* selection, uint* fetched) => Boundary(() =>
    {
        if (selection == null || fetched == null)
        {
            return PointerError;
        }

        *fetched = 0;
        if (!Readable)
        {
            return NoLock;
        }

        if (count == 0 || index is not (0 or uint.MaxValue))
        {
            return InvalidArgument;
        }

        TextSelection current = Current().Selection;
        *selection = new()
        {
            acpStart = Math.Min(current.Anchor, current.Active),
            acpEnd = Math.Max(current.Anchor, current.Active),
            style = new() { ase = current.Active < current.Anchor ? TsActiveSelEnd.TS_AE_START : TsActiveSelEnd.TS_AE_END, fInterimChar = false },
        };
        *fetched = 1;
        return 0;
    });

    /// <summary>Set Selection.</summary>
    /// <param name = "count">The count.</param>
    /// <param name = "selection">The selection.</param>
    /// <returns>The operation result.</returns>
    public int SetSelection(uint count, TsSelection* selection) => Boundary(() =>
    {
        if (!Writable)
        {
            return NoLock;
        }

        if (_isReadOnly())
        {
            return ReadOnly;
        }

        if (selection == null || count != 1 || selection->acpStart > selection->acpEnd)
        {
            return InvalidArgument;
        }

        _edit!.SetSelection(selection->style.ase == TsActiveSelEnd.TS_AE_START ? new(selection->acpEnd, selection->acpStart) : new(selection->acpStart, selection->acpEnd));
        return 0;
    });

    /// <summary>Get Text.</summary>
    /// <param name = "start">The start.</param>
    /// <param name = "end">The end.</param>
    /// <param name = "plain">The plain.</param>
    /// <param name = "requested">The requested.</param>
    /// <param name = "returned">The returned.</param>
    /// <param name = "runs">The runs.</param>
    /// <param name = "requestedRuns">The requested runs.</param>
    /// <param name = "returnedRuns">The returned runs.</param>
    /// <param name = "next">The next.</param>
    /// <returns>The operation result.</returns>
    public int GetText(int start, int end, char* plain, uint requested, uint* returned, TsRunInfo* runs, uint requestedRuns, uint* returnedRuns, int* next) => Boundary(() =>
    {
        if (returned == null || returnedRuns == null || next == null || (requested != 0 && plain == null) || (requestedRuns != 0 && runs == null))
        {
            return PointerError;
        }

        *returned = 0;
        *returnedRuns = 0;
        if (!Readable)
        {
            return NoLock;
        }

        string text = Current().Text;
        end = end == -1 ? text.Length : end;
        if (start < 0 || end < start || end > text.Length)
        {
            return InvalidPosition;
        }

        int length = (int)Math.Min(requested, end - start);
        text.AsSpan(start, length).CopyTo(new Span<char>(plain, length));
        *returned = (uint)length;
        *next = start + length;
        if (requestedRuns != 0)
        {
            *runs = new()
            {
                uCount = (uint)length,
                type = TsRunType.TS_RT_PLAIN,
            };
            *returnedRuns = length == 0 ? 0U : 1U;
        }

        return 0;
    });

    /// <summary>Set Text.</summary>
    /// <param name = "flags">The flags.</param>
    /// <param name = "start">The start.</param>
    /// <param name = "end">The end.</param>
    /// <param name = "text">The text.</param>
    /// <param name = "count">The count.</param>
    /// <param name = "change">The change.</param>
    /// <returns>The operation result.</returns>
    public int SetText(uint flags, int start, int end, char* text, uint count, TsTextChange* change) => Boundary(() =>
    {
        if (!Writable)
        {
            return NoLock;
        }

        if (_isReadOnly())
        {
            return ReadOnly;
        }

        if (change == null || count > _document.MaximumLength || (count != 0 && text == null))
        {
            return InvalidArgument;
        }

        string value = TextDocument.Normalize(new string(text, 0, (int)count));
        int caret = checked(start + value.Length);
        _edit!.Replace(new(start, end - start), value, new(caret, caret));
        *change = new()
        {
            acpStart = start,
            acpOldEnd = end,
            acpNewEnd = caret,
        };
        return 0;
    });

    /// <summary>Insert Text At Selection.</summary>
    /// <param name = "flags">The flags.</param>
    /// <param name = "text">The text.</param>
    /// <param name = "count">The count.</param>
    /// <param name = "start">The start.</param>
    /// <param name = "end">The end.</param>
    /// <param name = "change">The change.</param>
    /// <returns>The operation result.</returns>
    public int InsertTextAtSelection(uint flags, char* text, uint count, int* start, int* end, TsTextChange* change) => Boundary(() =>
    {
        bool queryOnly = (flags & 2) != 0;
        bool noQuery = (flags & 1) != 0;
        if ((flags & ~3u) != 0 || (queryOnly && noQuery))
        {
            return InvalidArgument;
        }

        if (!Readable || (!queryOnly && !Writable))
        {
            return NoLock;
        }

        if (!queryOnly && _isReadOnly())
        {
            return ReadOnly;
        }

        if (!noQuery && (start == null || end == null))
        {
            return PointerError;
        }

        TextSelection selection = Current().Selection;
        int a = Math.Min(selection.Anchor, selection.Active);
        int b = Math.Max(selection.Anchor, selection.Active);
        if (!noQuery && start != null)
        {
            *start = a;
        }

        if (!noQuery && end != null)
        {
            *end = checked(a + (int)count);
        }

        if (queryOnly)
        {
            int ignoredA;
            int ignoredB;
            return QueryInsert(a, b, count, &ignoredA, &ignoredB);
        }

        TsTextChange local;
        int result = SetText(0, a, b, text, count, change == null ? &local : change);
        if (!noQuery && end != null && result >= 0)
        {
            *end = Current().Selection.Active;
        }

        return result;
    });

    /// <summary>Get End ACP.</summary>
    /// <param name = "position">The position.</param>
    /// <returns>The operation result.</returns>
    public int GetEndACP(int* position) => Boundary(() =>
    {
        if (position == null)
        {
            return PointerError;
        }

        if (!Readable)
        {
            return NoLock;
        }

        *position = Current().Text.Length;
        return 0;
    });

    /// <summary>Get Active View.</summary>
    /// <param name = "view">The view.</param>
    /// <returns>The operation result.</returns>
    public int GetActiveView(uint* view) => Boundary(() =>
    {
        if (view == null)
        {
            return PointerError;
        }

        *view = 1;
        return 0;
    });

    /// <summary>Get Wnd.</summary>
    /// <param name = "view">The view.</param>
    /// <param name = "window">The window.</param>
    /// <returns>The operation result.</returns>
    public int GetWnd(uint view, nint* window) => Boundary(() =>
    {
        if (window == null)
        {
            return PointerError;
        }

        *window = _getWindow();
        return view == 1 && *window != 0 ? 0 : InvalidArgument;
    });

    /// <summary>Get Screen Ext.</summary>
    /// <param name = "view">The view.</param>
    /// <param name = "rectangle">The rectangle.</param>
    /// <returns>The operation result.</returns>
    public int GetScreenExt(uint view, TsfRect* rectangle) => Boundary(() =>
    {
        if (rectangle == null)
        {
            return PointerError;
        }

        if (view != 1 || !HasCurrentGeometry())
        {
            return NoLayout;
        }

        *rectangle = _toScreen(_geometry!.Viewport);
        return 0;
    });

    /// <summary>Get Text Ext.</summary>
    /// <param name = "view">The view.</param>
    /// <param name = "start">The start.</param>
    /// <param name = "end">The end.</param>
    /// <param name = "rectangle">The rectangle.</param>
    /// <param name = "clipped">The clipped.</param>
    /// <returns>The operation result.</returns>
    public int GetTextExt(uint view, int start, int end, TsfRect* rectangle, int* clipped) => Boundary(() =>
    {
        if (rectangle == null || clipped == null)
        {
            return PointerError;
        }

        *clipped = 0;
        if (!Readable)
        {
            return NoLock;
        }

        if (view != 1 || !HasCurrentGeometry())
        {
            return NoLayout;
        }

        if (!ValidRange(Current().Text, start, end))
        {
            return InvalidPosition;
        }

        WindowRect[] ranges = start == end ? _geometry!.CaretStops.Where(c => c.Position == start).Select(c => c.Bounds).Take(1).ToArray() : _geometry!.Ranges.Where(r => r.Range.Start < end && r.Range.Start + r.Range.Length > start).Select(r => r.Bounds).ToArray();
        if (ranges.Length == 0)
        {
            return NoLayout;
        }

        float x = ranges.Min(r => r.X);
        float y = ranges.Min(r => r.Y);
        float right = ranges.Max(r => r.X + r.Width);
        float bottom = ranges.Max(r => r.Y + r.Height);
        WindowRect viewport = _geometry!.Viewport;
        float a = MathF.Max(x, viewport.X);
        float b = MathF.Max(y, viewport.Y);
        float c = MathF.Min(right, viewport.X + viewport.Width);
        float d = MathF.Min(bottom, viewport.Y + viewport.Height);
        *clipped = a != x || b != y || c != right || d != bottom ? 1 : 0;
        *rectangle = _toScreen(new(a, b, MathF.Max(0, c - a), MathF.Max(0, d - b)));
        return 0;
    });

    /// <summary>Get ACPFrom Point.</summary>
    /// <param name = "view">The view.</param>
    /// <param name = "point">The point.</param>
    /// <param name = "flags">The flags.</param>
    /// <param name = "position">The position.</param>
    /// <returns>The operation result.</returns>
    public int GetACPFromPoint(uint view, TsfPoint* point, uint flags, int* position) => Boundary(() =>
    {
        if (point == null || position == null)
        {
            return PointerError;
        }

        if (!Readable)
        {
            return NoLock;
        }

        if (view != 1 || !HasCurrentGeometry() || _geometry!.CaretStops.Count == 0)
        {
            return NoLayout;
        }

        if ((flags & ~3u) != 0)
        {
            return InvalidArgument;
        }

        TsfPoint query = *point;
        TsfRect viewport = _toScreen(_geometry.Viewport);
        bool inside = query.X >= viewport.left && query.X < viewport.right && query.Y >= viewport.top && query.Y < viewport.bottom;
        if (!inside && (flags & 2) == 0)
        {
            return unchecked((int)0x80040207);
        }

        if (inside && (flags & 1) == 0)
        {
            foreach (TextInputRangeGeometry range in _geometry.Ranges)
            {
                TsfRect bounds = _toScreen(range.Bounds);
                if (query.X >= bounds.left && query.X < bounds.right && query.Y >= bounds.top && query.Y < bounds.bottom)
                {
                    *position = range.Range.Start;
                    return 0;
                }
            }

            if ((flags & 2) == 0)
            {
                return unchecked((int)0x80040207);
            }
        }

        TextInputCaretGeometry nearest = _geometry.CaretStops.MinBy(c =>
        {
            TsfRect r = _toScreen(c.Bounds);
            return Math.Abs((long)r.left - query.X) + Math.Abs((long)r.top - query.Y);
        })!;
        *position = nearest.Position;
        return 0;
    });

    /// <summary>Copies display attributes while the TSF read cookie is valid.</summary>
    public int OnEndEdit(nint contextPointer, uint readCookie, nint editRecord) => Boundary(() =>
    {
        TextCompositionSnapshot? composition = Current().Composition;
        if (composition is null || _composition == 0)
        {
            return 0;
        }

        IReadOnlyList<TextCompositionSpan> attributes = TsfDisplayAttributes.Read(contextPointer, readCookie, _composition, composition.Range);
        Apply(edit => edit.SetComposition(composition with { Spans = attributes }));
        return 0;
    });

    /// <summary>On Start Composition.</summary>
    /// <param name = "composition">The composition.</param>
    /// <param name = "accepted">The accepted.</param>
    /// <returns>The operation result.</returns>
    public int OnStartComposition(nint composition, int* accepted) => Boundary(() =>
    {
        if (accepted == null)
        {
            return PointerError;
        }

        *accepted = 0;
        if (_isReadOnly() || _composition != 0 || composition == 0)
        {
            return 0;
        }

        ComPtr<ITfCompositionView> view = TsfCom.Query<ITfCompositionView>(composition);
        nint range = 0;
        try
        {
            Marshal.ThrowExceptionForHR(view.Pointer->GetRange((ITfRange**)&range));
            TextSpan span = Range(range);
            _compositionId = Guid.NewGuid();
            Apply(edit => edit.SetComposition(new(_compositionId, span, [new(span, TextUnderline.Solid, true)])));
            ((IUnknown*)composition)->AddRef();
            _composition = composition;
            *accepted = 1;
            return 0;
        }
        finally
        {
            TsfCom.Return(range);
            TsfCom.Release(view);
        }
    });

    /// <summary>On Update Composition.</summary>
    /// <param name = "composition">The composition.</param>
    /// <param name = "range">The range.</param>
    /// <returns>The operation result.</returns>
    public int OnUpdateComposition(nint composition, nint range) => Boundary(() =>
    {
        if (_composition == 0 || range == 0)
        {
            return 0;
        }

        TextSpan span = Range(range);
        Apply(edit => edit.SetComposition(new(_compositionId, span, [new(span, TextUnderline.Solid, true)])));
        return 0;
    });

    /// <summary>On End Composition.</summary>
    /// <param name = "composition">The composition.</param>
    /// <returns>The operation result.</returns>
    public int OnEndComposition(nint composition) => Boundary(() =>
    {
        if (_composition != 0)
        {
            EndDocumentComposition(RequestedEnd ?? TextCompositionEnd.Commit);
            TsfCom.Return(_composition);
            _composition = 0;
            _compositionId = Guid.Empty;
        }

        return 0;
    });

    /// <summary>Get Formatted Text.</summary>
    /// <param name = "start">The start.</param>
    /// <param name = "end">The end.</param>
    /// <param name = "result">The result.</param>
    /// <returns>The operation result.</returns>
    public int GetFormattedText(int start, int end, nint* result) => EmptyObject(result);

    /// <summary>Get Embedded.</summary>
    /// <param name = "position">The position.</param>
    /// <param name = "service">The service.</param>
    /// <param name = "iid">The iid.</param>
    /// <param name = "result">The result.</param>
    /// <returns>The operation result.</returns>
    public int GetEmbedded(int position, Guid* service, Guid* iid, nint* result) => EmptyObject(result);

    /// <summary>Query Insert Embedded.</summary>
    /// <param name = "service">The service.</param>
    /// <param name = "format">The format.</param>
    /// <param name = "insertable">The insertable.</param>
    /// <returns>The operation result.</returns>
    public int QueryInsertEmbedded(Guid* service, nint format, int* insertable)
    {
        if (insertable == null)
        {
            return PointerError;
        }

        *insertable = 0;
        return 0;
    }

    /// <summary>Insert Embedded.</summary>
    /// <param name = "flags">The flags.</param>
    /// <param name = "start">The start.</param>
    /// <param name = "end">The end.</param>
    /// <param name = "data">The data.</param>
    /// <param name = "change">The change.</param>
    /// <returns>The operation result.</returns>
    public int InsertEmbedded(uint flags, int start, int end, nint data, TsTextChange* change) => Unsupported;

    /// <summary>Insert Embedded At Selection.</summary>
    /// <param name = "flags">The flags.</param>
    /// <param name = "data">The data.</param>
    /// <param name = "start">The start.</param>
    /// <param name = "end">The end.</param>
    /// <param name = "change">The change.</param>
    /// <returns>The operation result.</returns>
    public int InsertEmbeddedAtSelection(uint flags, nint data, int* start, int* end, TsTextChange* change) => Unsupported;

    /// <summary>Request Supported Attrs.</summary>
    /// <param name = "flags">The flags.</param>
    /// <param name = "count">The count.</param>
    /// <param name = "filter">The filter.</param>
    /// <returns>The operation result.</returns>
    public int RequestSupportedAttrs(uint flags, uint count, nint filter) => 0;

    /// <summary>Request Attrs At Position.</summary>
    /// <param name = "position">The position.</param>
    /// <param name = "count">The count.</param>
    /// <param name = "filter">The filter.</param>
    /// <param name = "flags">The flags.</param>
    /// <returns>The operation result.</returns>
    public int RequestAttrsAtPosition(int position, uint count, nint filter, uint flags) => 0;

    /// <summary>Request Attrs Transitioning At Position.</summary>
    /// <param name = "position">The position.</param>
    /// <param name = "count">The count.</param>
    /// <param name = "filter">The filter.</param>
    /// <param name = "flags">The flags.</param>
    /// <returns>The operation result.</returns>
    public int RequestAttrsTransitioningAtPosition(int position, uint count, nint filter, uint flags) => 0;

    /// <summary>Retrieve Requested Attrs.</summary>
    /// <param name = "count">The count.</param>
    /// <param name = "values">The values.</param>
    /// <param name = "fetched">The fetched.</param>
    /// <returns>The operation result.</returns>
    public int RetrieveRequestedAttrs(uint count, nint values, uint* fetched)
    {
        if (fetched == null)
        {
            return PointerError;
        }

        *fetched = 0;
        return 0;
    }

    /// <summary>Find Next Attr Transition.</summary>
    /// <param name = "start">The start.</param>
    /// <param name = "halt">The halt.</param>
    /// <param name = "count">The count.</param>
    /// <param name = "filter">The filter.</param>
    /// <param name = "flags">The flags.</param>
    /// <param name = "next">The next.</param>
    /// <param name = "found">The found.</param>
    /// <param name = "offset">The offset.</param>
    /// <returns>The operation result.</returns>
    public int FindNextAttrTransition(int start, int halt, uint count, nint filter, uint flags, int* next, int* found, int* offset)
    {
        if (next == null || found == null || offset == null)
        {
            return PointerError;
        }

        *next = halt;
        *found = 0;
        *offset = 0;
        return 0;
    }

    internal void UpdateGeometry(TextInputGeometry geometry)
    {
        _geometry = TextInputGeometryValidation.Copy(geometry);
        _layoutChanged = true;
    }

    internal void Advance()
    {
        if (_lock != 0)
        {
            return;
        }

        if (_pendingLock != 0)
        {
            uint pending = _pendingLock;
            _pendingLock = 0;
            GrantLock(pending);
        }

        TextDocumentSnapshot current = _document.CaptureSnapshot();
        TextDocumentSnapshot observed = _cancellationBefore ?? _lastObserved;
        if (_sink is null || _lock != 0)
        {
            _lastObserved = current;
            _cancellationBefore = null;
            return;
        }

        _notifying = true;
        try
        {
            if (current.Text != observed.Text && (_sinkMask & 1) != 0)
            {
                var change = new TsTextChange
                {
                    acpStart = 0,
                    acpOldEnd = observed.Text.Length,
                    acpNewEnd = current.Text.Length,
                };
                Marshal.ThrowExceptionForHR(_sink.Pointer->OnTextChange((TEXT_STORE_TEXT_CHANGE_FLAGS)0, (TS_TEXTCHANGE*)&change));
            }

            if (current.Selection != observed.Selection && (_sinkMask & 2) != 0)
            {
                Marshal.ThrowExceptionForHR(_sink.Pointer->OnSelectionChange());
            }

            if (_layoutChanged && (_sinkMask & 4) != 0)
            {
                _layoutChanged = false;
                Marshal.ThrowExceptionForHR(_sink.Pointer->OnLayoutChange((TsLayoutCode)0, 1));
            }

            _lastObserved = current;
            _cancellationBefore = null;
        }
        finally
        {
            _notifying = false;
        }
    }

    internal void EndDocumentComposition(TextCompositionEnd behavior)
    {
        TextDocumentSnapshot before = Current();
        Apply(edit => edit.EndComposition(behavior));
        if (behavior == TextCompositionEnd.Cancel && before.Text != Current().Text)
        {
            _cancellationBefore ??= before;
            _layoutChanged = true;
        }
    }

    internal void Release()
    {
        if (HasPendingWork || HasComposition)
        {
            throw new InvalidOperationException("Drain TSF locks and composition before releasing the store.");
        }

        TsfCom.Release(_sink);
        _sink = null;
        _sinkIdentity = 0;
    }

    private static int Boundary(Func<int> operation)
    {
        try
        {
            return operation();
        }
        catch (ArgumentException)
        {
            return InvalidArgument;
        }
        catch (Exception error)
        {
            return Marshal.GetHRForException(error);
        }
    }

    private static bool ValidRange(string text, int start, int end) => start >= 0 && end >= start && end <= text.Length && (start == 0 || start == text.Length || !char.IsLowSurrogate(text[start])) && (end == 0 || end == text.Length || !char.IsLowSurrogate(text[end]));

    private static nint Identity(nint pointer)
    {
        using ComPtr<IUnknown> identity = TsfCom.Query<IUnknown>(pointer);
        return (nint)identity.Pointer;
    }

    private static int EmptyObject(nint* pointer)
    {
        if (pointer == null)
        {
            return PointerError;
        }

        *pointer = 0;
        return Unsupported;
    }

    private static TextSpan Range(nint pointer)
    {
        ComPtr<ITfRangeACP> range = TsfCom.Query<ITfRangeACP>(pointer);
        try
        {
            int start;
            int length;
            Marshal.ThrowExceptionForHR(range.Pointer->GetExtent(&start, &length));
            return new(start, length);
        }
        finally
        {
            TsfCom.Release(range);
        }
    }

    private TextDocumentSnapshot Current() => _edit?.CaptureSnapshot() ?? _document.CaptureSnapshot();

    private bool HasCurrentGeometry() => _isVisible() && _geometry is not null && _geometry.DocumentRevision == _document.CaptureSnapshot().Revision && Current().Text == _document.CaptureSnapshot().Text;

    private int GrantLock(uint flags)
    {
        _lock = flags & 6;
        try
        {
            if (Writable || (_lock & 4) != 0)
            {
                _edit = _document.BeginEdit(TextEditOrigin.Platform);
            }

            int result = _sink!.Pointer->OnLockGranted((TEXT_STORE_LOCK_FLAGS)(flags & 6));
            if (result >= 0)
            {
                _edit?.Commit();
            }

            _lastObserved = _document.CaptureSnapshot();
            return result;
        }
        finally
        {
            _edit?.Dispose();
            _edit = null;
            _lock = 0;
        }
    }

    private void Apply(Action<TextDocumentTransaction> action)
    {
        if (_edit is not null)
        {
            action(_edit);
            return;
        }

        using TextDocumentTransaction edit = _document.BeginEdit(TextEditOrigin.Platform);
        action(edit);
        edit.Commit();
        _lastObserved = _document.CaptureSnapshot();
    }
}
