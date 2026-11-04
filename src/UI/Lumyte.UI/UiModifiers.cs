namespace Lumyte.UI;

/// <summary>Describes host-reported keyboard modifiers.</summary>
[Flags]
public enum UiModifiers
{
    /// <summary>No modifier is held.</summary>
    None = 0,

    /// <summary>Shift is held.</summary>
    Shift = 1,

    /// <summary>Control is held.</summary>
    Control = 2,

    /// <summary>Alt is held.</summary>
    Alt = 4,

    /// <summary>Meta is held.</summary>
    Meta = 8,
}
