namespace Lumyte.Interaction;

public sealed class RebindingSession
{
    private readonly ActionBindingDocument document;

    internal RebindingSession(ActionBindingDocument document, ActionBindingSlot slot)
    {
        this.document = document;
        Slot = slot;
    }

    public ActionBindingSlot Slot { get; }

    public RebindingCandidate? Candidate { get; private set; }

    public RebindingSessionStatus Status { get; private set; }

    public IReadOnlyList<ActionBindingConflict> Conflicts => Candidate is null
        ? []
        : document.FindConflicts(Slot, Candidate.Control);

    public bool TryOffer(RebindingCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        EnsureActive();
        if (candidate.ValueKind != Slot.ValueKind)
        {
            return false;
        }

        Candidate = candidate;
        Status = RebindingSessionStatus.CandidateReceived;
        return true;
    }

    public void Confirm()
    {
        EnsureActive();
        if (Candidate is null)
        {
            throw new InvalidOperationException("A rebinding candidate has not been received.");
        }

        document.SetControl(Slot, Candidate.Control);
        Status = RebindingSessionStatus.Confirmed;
    }

    public void Cancel()
    {
        EnsureActive();
        Status = RebindingSessionStatus.Canceled;
    }

    private void EnsureActive()
    {
        if (Status is RebindingSessionStatus.Confirmed or RebindingSessionStatus.Canceled)
        {
            throw new InvalidOperationException("The rebinding session has already ended.");
        }
    }
}
