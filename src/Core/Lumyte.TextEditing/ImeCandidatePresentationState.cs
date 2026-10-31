namespace Lumyte.TextEditing;

/// <summary>Defines ime candidate presentation state.</summary>
public enum ImeCandidatePresentationState
{
    /// <summary>System.</summary>
    System,

    /// <summary>Preparing.</summary>
    Preparing,

    /// <summary>Custom.</summary>
    Custom,

    /// <summary>Returning To System.</summary>
    ReturningToSystem,

    /// <summary>Closed.</summary>
    Closed,

    /// <summary>Failed.</summary>
    Failed,
}
