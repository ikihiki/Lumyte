namespace Lumyte.UI.Automation;

/// <summary>Classifies automation failures without parsing messages.</summary>
public enum UiAutomationFailureKind
{
    /// <summary>The single operation deadline expired.</summary>
    Timeout,

    /// <summary>A strict query matched multiple elements.</summary>
    AmbiguousMatch,

    /// <summary>The actual component does not support the requested action.</summary>
    UnsupportedAction,

    /// <summary>The target changed after input began.</summary>
    TargetChanged,

    /// <summary>Dispatch or application code failed after input began.</summary>
    ActionFailed,

    /// <summary>The borrowed host or surface is no longer available.</summary>
    HostUnavailable,
}
