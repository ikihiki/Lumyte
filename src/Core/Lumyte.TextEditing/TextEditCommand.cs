namespace Lumyte.TextEditing;

/// <summary>Defines text edit command.</summary>
public enum TextEditCommand
{
    /// <summary>Previous.</summary>
    Previous,

    /// <summary>Next.</summary>
    Next,

    /// <summary>Start.</summary>
    Start,

    /// <summary>End.</summary>
    End,

    /// <summary>Delete Backward.</summary>
    DeleteBackward,

    /// <summary>Delete Forward.</summary>
    DeleteForward,

    /// <summary>Select All.</summary>
    SelectAll,
}
