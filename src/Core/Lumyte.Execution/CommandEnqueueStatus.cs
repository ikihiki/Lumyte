namespace Lumyte.Execution;

/// <summary>Result of an atomic command admission attempt.</summary>
public enum CommandEnqueueStatus
{
    /// <summary>Accepted.</summary>
    Accepted,

    /// <summary>Canceled.</summary>
    Canceled,

    /// <summary>NotRunning.</summary>
    NotRunning,

    /// <summary>QueueFull.</summary>
    QueueFull,
}
