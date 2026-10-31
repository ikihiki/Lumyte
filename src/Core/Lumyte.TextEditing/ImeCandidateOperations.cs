namespace Lumyte.TextEditing;

/// <summary>Defines ime candidate operations.</summary>
[Flags]
public enum ImeCandidateOperations
{
    /// <summary>None.</summary>
    None = 0,

    /// <summary>Select.</summary>
    Select = 1,

    /// <summary>Page.</summary>
    Page = 2,

    /// <summary>Finalize.</summary>
    Finalize = 4,

    /// <summary>Abort.</summary>
    Abort = 8,
}
