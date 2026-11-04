namespace Lumyte.UI.Navigation;

/// <summary>Describes a settled request, independently of later retirement.</summary>
public enum UiNavigationStatus
{
    /// <summary>Committed.</summary>
    Committed,

    /// <summary>Rejected.</summary>
    Rejected,

    /// <summary>Canceled.</summary>
    Canceled,

    /// <summary>Failed.</summary>
    Failed,

    /// <summary>Busy.</summary>
    Busy,

    /// <summary>NoHistory.</summary>
    NoHistory,
}
