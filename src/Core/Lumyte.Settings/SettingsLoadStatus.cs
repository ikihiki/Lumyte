namespace Lumyte.Settings;

/// <summary>The initial loading outcome.</summary>
public enum SettingsLoadStatus
{
    /// <summary>Saved values were loaded.</summary>
    Loaded,

    /// <summary>No saved values existed.</summary>
    Defaults,

    /// <summary>Saved values were malformed or failed validation.</summary>
    InvalidData,

    /// <summary>The document or section version is unsupported.</summary>
    UnsupportedVersion,

    /// <summary>The storage medium could not be read.</summary>
    StorageFailure,
}
