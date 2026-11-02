namespace Lumyte.UI.Application;

/// <summary>Identifies a request to update the application's window model.</summary>
public enum UiWindowCloseReason
{
    /// <summary>The user requested closure.</summary>
    User,

    /// <summary>The owner is ending its lifetime.</summary>
    OwnerClosing,
}
