namespace Lumyte.UI.Navigation;

/// <summary>Describes navigation work and retained cleanup.</summary>
public enum UiNavigationPhase
{
    /// <summary>Idle.</summary>
    Idle,

    /// <summary>Preparing.</summary>
    Preparing,

    /// <summary>AwaitingGuard.</summary>
    AwaitingGuard,

    /// <summary>Quiescing.</summary>
    Quiescing,

    /// <summary>Retiring.</summary>
    Retiring,

    /// <summary>RetirementBlocked.</summary>
    RetirementBlocked,

    /// <summary>Stopping.</summary>
    Stopping,

    /// <summary>Stopped.</summary>
    Stopped,

    /// <summary>Faulted.</summary>
    Faulted,
}
