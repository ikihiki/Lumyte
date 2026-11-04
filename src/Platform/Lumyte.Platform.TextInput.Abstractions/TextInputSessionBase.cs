using Lumyte.TextEditing;

namespace Lumyte.Platform.TextInput.Abstractions;

/// <summary>Defines text input session base.</summary>
public abstract class TextInputSessionBase : ITextInputSession, ITextInputCandidateSession, ITextInputQuiescence
{
    private readonly Guid _id = Guid.NewGuid();
    private TextCompositionEnd? _ending;
    private bool _endingStarted;
    private bool _released;
    private TextInputGeometry? _geometry;

    /// <summary>Initializes a new instance of the <see cref = "TextInputSessionBase"/> class.</summary>
    /// <param name = "target">The target.</param>
    /// <param name = "document">The document.</param>
    /// <param name = "navigator">The navigator.</param>
    /// <param name = "generation">The generation.</param>
    protected TextInputSessionBase(TextInputTarget target, TextDocument document, ITextDocumentNavigator navigator, ulong generation)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(navigator);
        if (target.SurfaceId == Guid.Empty || target.EditorId == Guid.Empty || generation == 0)
        {
            throw new ArgumentException("A live editor identity and generation are required.");
        }

        Target = target;
        Document = document;
        Navigator = navigator;
        Generation = generation;
    }

    /// <summary>Gets the status.</summary>
    public TextInputSessionStatus Status => new(_id, Generation, !_released && _ending is null, Document.CaptureSnapshot().Composition is not null, _released ? "released" : _ending is null ? "active" : "ending");

    /// <summary>Gets a value indicating whether this option is enabled.</summary>
    public bool IsReleased => _released;

    /// <summary>Gets a value indicating whether this option is enabled.</summary>
    public virtual bool IsInputIdle => !_released && _ending is null && Document.CaptureSnapshot().Composition is null;

    /// <summary>Gets the candidate support.</summary>
    public virtual ImeCandidateSupport CandidateSupport => new(false, false, false, "system-ui-only");

    /// <summary>Gets the candidate state.</summary>
    public virtual ImeCandidateState CandidateState => new(null, _released ? ImeCandidatePresentationState.Closed : ImeCandidatePresentationState.System, "system-ui-only");

    /// <summary>Gets the target.</summary>
    public TextInputTarget Target { get; }

    /// <summary>Gets the document.</summary>
    public TextDocument Document { get; }

    /// <summary>Gets the navigator.</summary>
    public ITextDocumentNavigator Navigator { get; }

    /// <summary>Gets the generation.</summary>
    public ulong Generation { get; }

    /// <summary>Gets the geometry.</summary>
    public TextInputGeometry? Geometry => _geometry;

    /// <summary>Gets a value indicating whether this option is enabled.</summary>
    protected bool IsEnding => _ending is not null;

    /// <summary>Gets a value indicating whether this option is enabled.</summary>
    protected virtual bool CanRelease => true;

    /// <summary>Update Geometry.</summary>
    /// <param name = "geometry">The geometry.</param>
    public void UpdateGeometry(TextInputGeometry geometry)
    {
        ObjectDisposedException.ThrowIf(_released, this);
        TextInputGeometry next = TextInputGeometryValidation.Copy(geometry);
        if (next.DocumentRevision != Document.CaptureSnapshot().Revision)
        {
            throw new ArgumentException("Geometry does not match the current document.", nameof(geometry));
        }

        _geometry = next;
        OnGeometryChanged(next);
    }

    /// <summary>Request End.</summary>
    /// <param name = "behavior">The behavior.</param>
    public void RequestEnd(TextCompositionEnd behavior)
    {
        if (!_released)
        {
            _ending ??= behavior;
        }
    }

    /// <summary>Dispose.</summary>
    public void Dispose() => RequestEnd(TextCompositionEnd.Cancel);

    /// <summary>Configure Candidate Ui.</summary>
    /// <param name = "policy">The policy.</param>
    /// <param name = "prepared">The prepared.</param>
    public virtual void ConfigureCandidateUi(ImeCandidateUiPolicy policy, bool prepared) => ObjectDisposedException.ThrowIf(_released, this);

    /// <summary>Capture Candidates.</summary>
    /// <returns>The operation result.</returns>
    public virtual ImeCandidateSnapshot? CaptureCandidates() => null;

    /// <summary>Request Candidate Async.</summary>
    /// <param name = "action">The action.</param>
    /// <param name = "cancellationToken">The cancellation token.</param>
    /// <returns>The operation result.</returns>
    public virtual ValueTask<ImeCandidateResult> RequestCandidateAsync(ImeCandidateAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new ImeCandidateResult(action, false, "unsupported-operation"));
    }

    /// <summary>Return Candidates To System Async.</summary>
    /// <param name = "cancellationToken">The cancellation token.</param>
    /// <returns>The operation result.</returns>
    public virtual ValueTask ReturnCandidatesToSystemAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    /// <summary>Advance.</summary>
    public void Advance()
    {
        if (_released)
        {
            return;
        }

        AdvanceCore();
        if (_ending is not { } ending)
        {
            return;
        }

        if (!_endingStarted)
        {
            _endingStarted = true;
            BeginEnding(ending);
        }

        if (!CanRelease)
        {
            return;
        }

        using (TextDocumentTransaction edit = Document.BeginEdit(TextEditOrigin.Platform))
        {
            edit.EndComposition(ending);
            edit.Commit();
        }

        ReleaseCore();
        _geometry = null;
        _released = true;
    }

    /// <summary>On Geometry Changed.</summary>
    /// <param name = "geometry">The geometry.</param>
    protected virtual void OnGeometryChanged(TextInputGeometry geometry)
    {
    }

    /// <summary>Advance Core.</summary>
    protected virtual void AdvanceCore()
    {
    }

    /// <summary>Begin Ending.</summary>
    /// <param name = "behavior">The behavior.</param>
    protected virtual void BeginEnding(TextCompositionEnd behavior)
    {
    }

    /// <summary>Release Core.</summary>
    protected abstract void ReleaseCore();
}
