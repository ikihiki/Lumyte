using System.Runtime.InteropServices;
using Lumyte.Platform.TextInput.Abstractions;
using Lumyte.Platform.Windows.TextInput.Interop;
using Lumyte.TextEditing;
using Lumyte.Windowing.Abstractions;

namespace Lumyte.Platform.Windows.TextInput;

internal sealed unsafe class WindowsTextSession : TextInputSessionBase
{
    private readonly ComPtr<ITfThreadMgr> _threadManager;
    private readonly WindowsWindow _focus;
    private readonly WindowsWindow _presentation;
    private readonly TsfTextStore _store;
    private readonly WindowsCandidateSource _candidates;
    private ComPtr<ITfDocumentMgr>? _documentManager;
    private ComPtr<ITfContext>? _context;
    private ComPtr<ITfSource>? _editSource;
    private uint _editCookie;
    private bool _editAdvised;
    private nint _documentPointer;
    private nint _previousAssociation;
    private nint _previousFocus;
    private bool _associated;
    private bool _focused;

    private bool _pushed;

    internal WindowsTextSession(ComPtr<ITfThreadMgr> threadManager, WindowsCandidateSource candidates, uint client, WindowsWindow focus, WindowsWindow presentation, TextInputTarget target, TextDocument document, ITextDocumentNavigator navigator, ulong generation, Func<bool> inputReadOnly)
        : base(target, document, navigator, generation)
    {
        _threadManager = threadManager;
        _candidates = candidates;
        _focus = focus;
        _presentation = presentation;
        _store = new(document, ToScreen, () => _presentation.Hwnd, isReadOnly: inputReadOnly, isVisible: () => _presentation.State.IsVisible && _presentation.State.ClientSize.X > 0 && _presentation.State.ClientSize.Y > 0);
        try
        {
            nint manager;
            Marshal.ThrowExceptionForHR(threadManager.Pointer->CreateDocumentMgr((ITfDocumentMgr**)&manager));
            _documentManager = TsfCom.Own<ITfDocumentMgr>(manager);
            _documentPointer = TsfCom.Borrow(_documentManager);
            nint textStore = TsfCom.Borrow<ITextStoreACP>(_store);
            try
            {
                nint context;
                uint cookie;
                Marshal.ThrowExceptionForHR(_documentManager.Pointer->CreateContext(client, 0, (IUnknown*)textStore, (ITfContext**)&context, &cookie));
                _context = TsfCom.Own<ITfContext>(context);
                _editSource = TsfCom.Query<ITfSource>(context);
                nint editSink = TsfCom.Borrow<ITfTextEditSink>(_store);
                try
                {
                    Guid editIid = TsfCom.GuidOf<ITfTextEditSink>();
                    uint editCookie;
                    Marshal.ThrowExceptionForHR(_editSource.Pointer->AdviseSink(&editIid, (IUnknown*)editSink, &editCookie));
                    _editCookie = editCookie;
                    _editAdvised = true;
                }
                finally
                {
                    TsfCom.Return(editSink);
                }

                nint borrowed = TsfCom.Borrow(_context);
                try
                {
                    Marshal.ThrowExceptionForHR(_documentManager.Pointer->Push((ITfContext*)borrowed));
                    _pushed = true;
                }
                finally
                {
                    TsfCom.Return(borrowed);
                }
            }
            finally
            {
                TsfCom.Return(textStore);
            }

            nint previousFocus;
            Marshal.ThrowExceptionForHR(threadManager.Pointer->GetFocus((ITfDocumentMgr**)&previousFocus));
            _previousFocus = previousFocus;
            nint previousAssociation;
            Marshal.ThrowExceptionForHR(threadManager.Pointer->AssociateFocus((HWND)focus.Hwnd, (ITfDocumentMgr*)_documentPointer, (ITfDocumentMgr**)&previousAssociation));
            _previousAssociation = previousAssociation;
            _associated = true;
            Marshal.ThrowExceptionForHR(threadManager.Pointer->SetFocus((ITfDocumentMgr*)_documentPointer));
            _focused = true;
        }
        catch
        {
            ReleasePartial();
            throw;
        }
    }

    /// <summary>Gets a value indicating whether this option is enabled.</summary>
    public override bool IsInputIdle => base.IsInputIdle && !_store.HasPendingWork && _candidates.IsIdle;

    /// <summary>Gets the candidate support.</summary>
    public override ImeCandidateSupport CandidateSupport => _candidates.Support;

    /// <summary>Gets the candidate state.</summary>
    public override ImeCandidateState CandidateState => _candidates.State;

    internal bool IsCandidateInputIdle => Status.IsActive && !_store.HasPendingWork && _candidates.IsIdle;

    internal nint DocumentPointer => _documentPointer;

    /// <summary>Gets a value indicating whether this option is enabled.</summary>
    protected override bool CanRelease => !_store.HasPendingWork && !_store.HasComposition;

    /// <summary>Configure Candidate Ui.</summary>
    /// <param name = "policy">The policy.</param>
    /// <param name = "prepared">The prepared.</param>
    public override void ConfigureCandidateUi(ImeCandidateUiPolicy policy, bool prepared) => _candidates.Configure(this, policy, prepared);

    /// <summary>Capture Candidates.</summary>
    /// <returns>The operation result.</returns>
    public override ImeCandidateSnapshot? CaptureCandidates() => _candidates.Capture(this);

    /// <summary>Request Candidate Async.</summary>
    /// <param name = "action">The action.</param>
    /// <param name = "cancellationToken">The cancellation token.</param>
    /// <returns>The operation result.</returns>
    public override ValueTask<ImeCandidateResult> RequestCandidateAsync(ImeCandidateAction action, CancellationToken cancellationToken = default) => _candidates.Request(this, action, cancellationToken);

    /// <summary>Return Candidates To System Async.</summary>
    /// <param name = "cancellationToken">The cancellation token.</param>
    /// <returns>The operation result.</returns>
    public override ValueTask ReturnCandidatesToSystemAsync(CancellationToken cancellationToken = default) => _candidates.ReturnToSystem();

    /// <summary>Advance Core.</summary>
    protected override void AdvanceCore() => _store.Advance();

    /// <summary>On Geometry Changed.</summary>
    /// <param name = "geometry">The geometry.</param>
    protected override void OnGeometryChanged(TextInputGeometry geometry) => _store.UpdateGeometry(geometry);

    /// <summary>Begin Ending.</summary>
    /// <param name = "behavior">The behavior.</param>
    protected override void BeginEnding(TextCompositionEnd behavior)
    {
        _store.RequestedEnd = behavior;
        if (_context is not null && _store.HasComposition)
        {
            nint pointer = TsfCom.Borrow(_context);
            ComPtr<ITfContextOwnerCompositionServices> composition = TsfCom.Query<ITfContextOwnerCompositionServices>(pointer);
            try
            {
                Marshal.ThrowExceptionForHR(composition.Pointer->TerminateComposition((ITfCompositionView*)_store.CompositionPointer));
            }
            finally
            {
                TsfCom.Release(composition);
                TsfCom.Return(pointer);
            }
        }
    }

    /// <summary>Release Core.</summary>
    protected override void ReleaseCore()
    {
        _store.Release();
        ReleasePartial();
    }

    private TsfRect ToScreen(WindowRect rect)
    {
        TsfPoint origin = default;
        if (TsfNative.ClientToScreen(_presentation.Hwnd, ref origin) == 0)
        {
            throw new InvalidOperationException("The native text presentation is unavailable.");
        }

        System.Numerics.Vector2 scale = _presentation.State.Scale;
        return new()
        {
            left = checked(origin.X + (int)MathF.Floor(rect.X * scale.X)),
            top = checked(origin.Y + (int)MathF.Floor(rect.Y * scale.Y)),
            right = checked(origin.X + (int)MathF.Ceiling((rect.X + rect.Width) * scale.X)),
            bottom = checked(origin.Y + (int)MathF.Ceiling((rect.Y + rect.Height) * scale.Y)),
        };
    }

    private void ReleasePartial()
    {
        if (_associated && _focus.State.Lifetime != WindowLifetime.Closed)
        {
            nint replaced;
            Marshal.ThrowExceptionForHR(_threadManager.Pointer->AssociateFocus((HWND)_focus.Hwnd, (ITfDocumentMgr*)_previousAssociation, (ITfDocumentMgr**)&replaced));
            TsfCom.Return(replaced);
            _associated = false;
        }

        if (_focused)
        {
            nint current;
            Marshal.ThrowExceptionForHR(_threadManager.Pointer->GetFocus((ITfDocumentMgr**)&current));
            try
            {
                if (current == _documentPointer)
                {
                    Marshal.ThrowExceptionForHR(_threadManager.Pointer->SetFocus((ITfDocumentMgr*)_previousFocus));
                }
            }
            finally
            {
                TsfCom.Return(current);
            }

            _focused = false;
        }

        if (_editAdvised)
        {
            Marshal.ThrowExceptionForHR(_editSource!.Pointer->UnadviseSink(_editCookie));
            _editAdvised = false;
        }

        TsfCom.Release(_editSource);
        _editSource = null;
        if (_pushed)
        {
            Marshal.ThrowExceptionForHR(_documentManager!.Pointer->Pop(1));
            _pushed = false;
        }

        TsfCom.Release(_context);
        _context = null;
        TsfCom.Return(_documentPointer);
        _documentPointer = 0;
        TsfCom.Release(_documentManager);
        _documentManager = null;
        TsfCom.Return(_previousAssociation);
        _previousAssociation = 0;
        TsfCom.Return(_previousFocus);
        _previousFocus = 0;
    }
}
