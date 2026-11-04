namespace Lumyte.UI;

/// <summary>Classifies component configuration changes without mutating declaration setters.</summary>
[Flags]
public enum UiInvalidation
{
    /// <summary>No runtime work is required.</summary>
    None = 0,

    /// <summary>Geometry and dependent input and visual information must be updated.</summary>
    Layout = 1,

    /// <summary>Input eligibility or ordering must be updated.</summary>
    Input = 2,

    /// <summary>Recorded visual content is no longer equivalent.</summary>
    Visual = 4,

    /// <summary>Conservatively invalidate all component-dependent state.</summary>
    All = Layout | Input | Visual,
}
