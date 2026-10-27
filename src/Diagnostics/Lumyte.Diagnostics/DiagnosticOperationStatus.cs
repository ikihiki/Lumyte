namespace Lumyte.Diagnostics;

/// <summary>A typed view of the existing diagnostic result status strings.</summary>
public enum DiagnosticOperationStatus
{
    /// <summary>The status is not recognized by this version.</summary>
    Unknown,

    /// <summary>The operation completed successfully.</summary>
    Success,

    /// <summary>The operation was rejected.</summary>
    Rejected,

    /// <summary>The expected revision conflicts with the current revision.</summary>
    Conflict,
}
