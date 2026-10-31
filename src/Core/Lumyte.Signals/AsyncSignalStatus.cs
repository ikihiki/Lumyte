namespace Lumyte.Signals;

/// <summary>Describes one asynchronous signal operation.</summary>
public enum AsyncSignalStatus
{
    /// <summary>The current operation is still running.</summary>
    Pending,

    /// <summary>The current operation produced a value, including null or default.</summary>
    Succeeded,

    /// <summary>The current operation failed.</summary>
    Failed,

    /// <summary>The current operation was canceled.</summary>
    Canceled,
}
