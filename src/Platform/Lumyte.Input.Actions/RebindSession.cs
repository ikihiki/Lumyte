using System.Collections.Immutable;

namespace Lumyte.Input.Actions;

/// <summary>Captures a binding candidate without changing the running profile.</summary>
public sealed class RebindSession
{
    private readonly ActionSystem _owner;

    internal RebindSession(ActionSystem owner, string bindingId, RebindOptions options, TimeSpan started)
    {
        _owner = owner;
        BindingId = bindingId;
        Options = options;
        Started = started;
    }

    /// <summary>Gets binding id.</summary>
    public string BindingId { get; }

    /// <summary>Gets candidate.</summary>
    public InputControl? Candidate { get; internal set; }

    /// <summary>Gets conflicts.</summary>
    public ImmutableArray<string> Conflicts { get; internal set; } = [];

    /// <summary>Gets a value indicating whether capture has finished.</summary>
    public bool IsComplete { get; internal set; }

    internal RebindOptions Options { get; }

    internal TimeSpan Started { get; }

    /// <summary>Creates a candidate profile without applying it.</summary>
    /// <param name = "policy">The policy value.</param>
    /// <returns>The result of the operation.</returns>
    public ActionProfile PrepareProfile(RebindConflictPolicy policy) => _owner.PrepareRebind(this, policy);

    /// <summary>Queues the candidate profile for application.</summary>
    /// <param name = "policy">The policy value.</param>
    public void Confirm(RebindConflictPolicy policy)
    {
        ActionProfile profile = PrepareProfile(policy);
        _owner.ApplyProfile(profile);
        IsComplete = true;
    }

    /// <summary>Ends capture without modifying the profile.</summary>
    public void Cancel() => _owner.CancelRebind(this);
}
