namespace Lumyte.Graphics.Planning;

/// <summary>Declares graphics recovery state.</summary>
public enum GraphicsRecoveryState
{
    /// <summary>Declares prepared.</summary>
    Prepared,

    /// <summary>Declares recording.</summary>
    Recording,

    /// <summary>Declares recorded.</summary>
    Recorded,

    /// <summary>Declares finished.</summary>
    Finished,

    /// <summary>Declares submitted.</summary>
    Submitted,

    /// <summary>Declares completed.</summary>
    Completed,

    /// <summary>Declares quarantined.</summary>
    Quarantined,

    /// <summary>Declares cleanup pending.</summary>
    CleanupPending,

    /// <summary>Declares retired.</summary>
    Retired,
}
