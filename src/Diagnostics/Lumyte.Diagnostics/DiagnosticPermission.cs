namespace Lumyte.Diagnostics;

/// <summary>Permissions required to invoke an operation.</summary>
public enum DiagnosticPermission
{
    /// <summary>Read engine state.</summary>
    Observe,

    /// <summary>Modify engine state.</summary>
    Edit,

    /// <summary>Override engine input.</summary>
    OverrideInput,
}
