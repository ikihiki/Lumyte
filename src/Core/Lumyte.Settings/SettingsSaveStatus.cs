namespace Lumyte.Settings;

/// <summary>The outcome of a write operation.</summary>
public enum SettingsSaveStatus
{
    /// <summary>The candidate was committed.</summary>
    Saved,

    /// <summary>The candidate failed validation or serialization.</summary>
    ValidationFailed,

    /// <summary>The edited revision is stale.</summary>
    Conflict,

    /// <summary>Storage rejected the write.</summary>
    StorageFailure,

    /// <summary>Explicit recovery is required before writing.</summary>
    RecoveryRequired,
}
