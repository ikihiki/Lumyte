namespace Lumyte.UI.Navigation;

/// <summary>The outcome of optional presentation effects.</summary>
public enum UiVisualTransitionStatus
{
    /// <summary>Completed.</summary>
    Completed,

    /// <summary>Skipped.</summary>
    Skipped,

    /// <summary>Interrupted.</summary>
    Interrupted,

    /// <summary>NotStarted.</summary>
    NotStarted,

    /// <summary>Failed.</summary>
    Failed,
}
