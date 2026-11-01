namespace Lumyte.UI;

/// <summary>Chooses a surface-local focus candidate.</summary>
public enum UiFocusNavigation
{
    /// <summary>The first candidate.</summary>
    First,

    /// <summary>The last candidate.</summary>
    Last,

    /// <summary>The next candidate, wrapping.</summary>
    Next,

    /// <summary>The previous candidate, wrapping.</summary>
    Previous,
}
