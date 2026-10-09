namespace Lumyte.Diagnostics.Sample;

/// <summary>A small owner-thread input domain used to demonstrate expiring overrides.</summary>
public sealed class InputOverrideService
{
    private readonly TimeProvider _clock;
    private readonly Dictionary<string, Lease> _leases = new(StringComparer.Ordinal);
    private readonly int _owner = Environment.CurrentManagedThreadId;

    /// <summary>Initializes a new instance of the <see cref="InputOverrideService"/> class.</summary>
    /// <param name="clock">The monotonic clock.</param>
    public InputOverrideService(TimeProvider clock)
    {
        _clock = clock;
    }

    /// <summary>Applies an owned, expiring override.</summary>
    /// <param name="context">The trusted execution context.</param>
    /// <param name="button">The logical button.</param>
    /// <param name="pressed">The state.</param>
    /// <param name="durationMs">The duration.</param>
    /// <returns>The lease.</returns>
    public DiagnosticResult<ButtonOverrideReceipt> Override(DiagnosticOperationContext context, string button, bool pressed, long durationMs)
    {
        CheckOwner();
        Prune();
        if (button != "Jump" || durationMs is < 1 or > 5000)
        {
            return DiagnosticResult<ButtonOverrideReceipt>.Reject("invalid-input", "Unknown button or invalid duration.");
        }

        if (_leases.TryGetValue(button, out Lease? existing) && (existing.Actor != context.ActorId || existing.Session != context.SessionId))
        {
            return DiagnosticResult<ButtonOverrideReceipt>.Reject("input-busy", "Button is owned by another actor.");
        }

        var id = Guid.NewGuid();
        _leases[button] = new(id, context.SessionId, context.ActorId, pressed, _clock.GetTimestamp() + ((durationMs * _clock.TimestampFrequency) / 1000));
        return DiagnosticResult<ButtonOverrideReceipt>.Success(new(id.ToString("D")));
    }

    /// <summary>Combines real input with the active override.</summary>
    /// <param name="button">The logical button.</param>
    /// <param name="physicalState">The real input state.</param>
    /// <returns>The resulting state.</returns>
    public bool Read(string button, bool physicalState)
    {
        CheckOwner();
        Prune();
        return _leases.TryGetValue(button, out Lease? lease) ? lease.Pressed : physicalState;
    }

    /// <summary>Releases every lease associated with a disconnected session.</summary>
    /// <param name="session">The stopped session.</param>
    public void ReleaseSession(Guid session)
    {
        CheckOwner();
        foreach (string button in _leases.Where(pair => pair.Value.Session == session).Select(pair => pair.Key).ToArray())
        {
            _leases.Remove(button);
        }
    }

    private void Prune()
    {
        foreach (string button in _leases.Where(pair => pair.Value.Deadline <= _clock.GetTimestamp()).Select(pair => pair.Key).ToArray())
        {
            _leases.Remove(button);
        }
    }

    private void CheckOwner()
    {
        if (Environment.CurrentManagedThreadId != _owner)
        {
            throw new InvalidOperationException("Input must run on its owning thread.");
        }
    }

    private sealed record Lease(Guid Id, Guid Session, string Actor, bool Pressed, long Deadline);
}
