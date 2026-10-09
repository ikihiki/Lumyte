namespace Lumyte.Input.Actions;

/// <summary>Defines rebind conflict policy.</summary>
public enum RebindConflictPolicy
{
    /// <summary>Selects reject.</summary>
    Reject,

    /// <summary>Selects allow.</summary>
    Allow,

    /// <summary>Selects replace conflicts.</summary>
    ReplaceConflicts,
}
